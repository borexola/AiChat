using AiChat.Config;
using AiChat.Data;
using AiChat.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using StringBuilder = System.Text.StringBuilder;

namespace AiChat.Services;

public class ChatService : IChatService
{
    private readonly string _deploymentName;
    private readonly IConversationStore _store;
    private readonly IDbContextFactory<ChatDbContext> _contextFactory;
    private readonly Uri _endpoint;
    private readonly ApiKeyCredential _credential;
    private readonly IReadOnlyList<DeploymentOption> _deployments;

    public string DefaultModelId => _deploymentName;

    public ChatService(
        IOptions<AppSettings> settings,
        IConversationStore store,
        IDbContextFactory<ChatDbContext> contextFactory)
    {
        _deploymentName = settings.Value.Azure.DefaultDeployment
            ?? throw new InvalidOperationException("Azure:DefaultDeployment must be configured with a valid Azure OpenAI deployment name.");
        _store = store;
        _contextFactory = contextFactory;

        var endpoint = settings.Value.Azure.Endpoint
            ?? throw new InvalidOperationException("Azure:Endpoint must be configured.");
        var apiKey = settings.Value.Azure.ApiKey
            ?? throw new InvalidOperationException("Azure:ApiKey must be configured.");

        _endpoint = NormalizeEndpoint(endpoint);
        _credential = new ApiKeyCredential(apiKey);

        var configuredDeployments = settings.Value.Azure.Deployments;
        _deployments = configuredDeployments.Count > 0
            ? configuredDeployments
            : new List<DeploymentOption> { new() { Id = _deploymentName, DisplayName = _deploymentName } };
    }

    public IReadOnlyList<DeploymentOption> GetAvailableDeployments() => _deployments;

    private ChatClient GetClient(string? modelId)
    {
        var model = string.IsNullOrEmpty(modelId) ? _deploymentName : modelId;
        return new ChatClient(model: model, credential: _credential,
            options: new OpenAIClientOptions { Endpoint = _endpoint });
    }

    public async Task<Guid> StartChatAsync(string userId, string message, string? systemPrompt = null)
    {
        var chat = await _store.CreateChatAsync(userId, systemPrompt: systemPrompt);
        return chat.Id;
    }

    public IAsyncEnumerable<string> SendMessageAsync(Guid chatId, string userMessage, CancellationToken ct, string? modelId = null) =>
        SendMessageAsync(chatId, userMessage, Array.Empty<ChatAttachment>(), ct, modelId);

    public async IAsyncEnumerable<string> SendMessageAsync(Guid chatId, string userMessage, IReadOnlyList<ChatAttachment> attachments, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, string? modelId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var chat = await context.Chats
            .Include(c => c.Messages)
            .Include(c => c.DocumentChunks)
            .FirstOrDefaultAsync(c => c.Id == chatId, ct);

        if (chat == null)
            throw new KeyNotFoundException($"Chat {chatId} not found");

        await _store.AddMessageAsync(new Data.Models.ChatMessage
        {
            ChatId = chatId,
            Role = "user",
            Content = userMessage
        });

        var messages = BuildMessages(chat, userMessage, currentAttachments: attachments).ToList();
        var client = GetClient(modelId);
        var streaming = client.CompleteChatStreaming(messages, new ChatCompletionOptions { MaxOutputTokenCount = 8192 }, ct);

        var assistantResponse = new StringBuilder();
        ClientResultException? notFoundException = null;
        bool cancelled = false;

        using var enumerator = streaming.GetEnumerator();
        while (true)
        {
            StreamingChatCompletionUpdate? chunk = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!enumerator.MoveNext()) break;
                chunk = enumerator.Current;
            }
            catch (OperationCanceledException) { cancelled = true; break; }
            catch (ClientResultException ex) when (ex.Status == 404) { notFoundException = ex; break; }

            if (chunk?.ContentUpdate is { })
            {
                foreach (var part in chunk.ContentUpdate)
                {
                    var text = part.Text;
                    if (string.IsNullOrEmpty(text)) continue;
                    assistantResponse.Append(text);
                    yield return text;
                }
            }
        }

        if (notFoundException is not null)
            throw new InvalidOperationException(
                "Azure OpenAI returned 404. Verify Azure:Endpoint uses your Azure OpenAI resource endpoint, for example https://<resource-name>.openai.azure.com/openai/v1/, and Azure:DeploymentName matches an existing deployment name.",
                notFoundException);

        if (assistantResponse.Length > 0)
        {
            await _store.AddMessageAsync(new Data.Models.ChatMessage
            {
                ChatId = chatId,
                Role = "assistant",
                ModelId = string.IsNullOrEmpty(modelId) ? _deploymentName : modelId,
                Content = assistantResponse.ToString()
            });
        }

        if (cancelled) yield break;
    }

    public async IAsyncEnumerable<string> RegenerateAsync(Guid chatId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, string? modelId = null)
    {
        // Delete the last assistant message from the DB
        await using (var ctx = await _contextFactory.CreateDbContextAsync(ct))
        {
            var lastAssistant = await ctx.Messages
                .Where(m => m.ChatId == chatId && m.Role == "assistant")
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (lastAssistant != null)
            {
                ctx.Messages.Remove(lastAssistant);
                await ctx.SaveChangesAsync(ct);
            }
        }

        // Reload chat with updated history (no last assistant message)
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var chat = await context.Chats
            .Include(c => c.Messages)
            .Include(c => c.DocumentChunks)
            .FirstOrDefaultAsync(c => c.Id == chatId, ct);

        if (chat == null)
            throw new KeyNotFoundException($"Chat {chatId} not found");

        // Use the last user message as the prompt
        var lastUser = chat.Messages
            .Where(m => m.Role == "user")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        if (lastUser == null)
            yield break;

        var messages = BuildMessages(chat, lastUser.Content, skipLastUser: true).ToList();
        var client = GetClient(modelId);
        var streaming = client.CompleteChatStreaming(messages, new ChatCompletionOptions { MaxOutputTokenCount = 8192 }, ct);

        var assistantResponse = new StringBuilder();
        ClientResultException? notFoundException = null;
        bool cancelled = false;

        using var enumerator = streaming.GetEnumerator();
        while (true)
        {
            StreamingChatCompletionUpdate? chunk = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!enumerator.MoveNext()) break;
                chunk = enumerator.Current;
            }
            catch (OperationCanceledException) { cancelled = true; break; }
            catch (ClientResultException ex) when (ex.Status == 404) { notFoundException = ex; break; }

            if (chunk?.ContentUpdate is { })
            {
                foreach (var part in chunk.ContentUpdate)
                {
                    var text = part.Text;
                    if (string.IsNullOrEmpty(text)) continue;
                    assistantResponse.Append(text);
                    yield return text;
                }
            }
        }

        if (notFoundException is not null)
            throw new InvalidOperationException("Azure OpenAI returned 404.", notFoundException);

        if (assistantResponse.Length > 0)
        {
            await _store.AddMessageAsync(new Data.Models.ChatMessage
            {
                ChatId = chatId,
                Role = "assistant",
                ModelId = string.IsNullOrEmpty(modelId) ? _deploymentName : modelId,
                Content = assistantResponse.ToString()
            });
        }

        if (cancelled) yield break;
    }

    /// <summary>
    /// Streams an assistant response based on the current chat history as-is,
    /// without deleting or adding any user messages. Used after an edit when
    /// all subsequent messages have already been cleared.
    /// </summary>
    public async IAsyncEnumerable<string> RespondToCurrentHistoryAsync(Guid chatId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, string? modelId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var chat = await context.Chats
            .Include(c => c.Messages)
            .Include(c => c.DocumentChunks)
            .FirstOrDefaultAsync(c => c.Id == chatId, ct);

        if (chat == null)
            throw new KeyNotFoundException($"Chat {chatId} not found");

        var lastUser = chat.Messages
            .Where(m => m.Role == "user")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        if (lastUser == null)
            yield break;

        var messages = BuildMessages(chat, lastUser.Content, skipLastUser: true).ToList();
        var client = GetClient(modelId);
        var streaming = client.CompleteChatStreaming(messages, new ChatCompletionOptions { MaxOutputTokenCount = 8192 }, ct);

        var assistantResponse = new StringBuilder();
        ClientResultException? notFoundException = null;
        bool cancelled = false;

        using var enumerator = streaming.GetEnumerator();
        while (true)
        {
            StreamingChatCompletionUpdate? chunk = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!enumerator.MoveNext()) break;
                chunk = enumerator.Current;
            }
            catch (OperationCanceledException) { cancelled = true; break; }
            catch (ClientResultException ex) when (ex.Status == 404) { notFoundException = ex; break; }

            if (chunk?.ContentUpdate is { })
            {
                foreach (var part in chunk.ContentUpdate)
                {
                    var text = part.Text;
                    if (string.IsNullOrEmpty(text)) continue;
                    assistantResponse.Append(text);
                    yield return text;
                }
            }
        }

        if (notFoundException is not null)
            throw new InvalidOperationException("Azure OpenAI returned 404.", notFoundException);

        if (assistantResponse.Length > 0)
        {
            await _store.AddMessageAsync(new Data.Models.ChatMessage
            {
                ChatId = chatId,
                Role = "assistant",
                ModelId = string.IsNullOrEmpty(modelId) ? _deploymentName : modelId,
                Content = assistantResponse.ToString()
            });
        }

        if (cancelled) yield break;
    }

    public async Task<string> GenerateTitleAsync(string firstUserMessage, CancellationToken ct)
    {
        var client = GetClient(null);
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            OpenAI.Chat.ChatMessage.CreateSystemMessage(
                "Generate a concise chat title (4 words or fewer, no punctuation, no quotes) that summarises the user's message. Reply with only the title."),
            OpenAI.Chat.ChatMessage.CreateUserMessage(firstUserMessage)
        };

        try
        {
            var result = await client.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 20 }, ct);
            var title = result.Value.Content[0].Text.Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(title) ? TruncateTitle(firstUserMessage) : title;
        }
        catch
        {
            return TruncateTitle(firstUserMessage);
        }
    }

    private static string TruncateTitle(string message) =>
        message.Length <= 50 ? message : message[..47] + "…";

    private static IEnumerable<OpenAI.Chat.ChatMessage> BuildMessages(
        Chat chat,
        string currentMessage,
        bool skipLastUser = false,
        IReadOnlyList<ChatAttachment>? currentAttachments = null)
    {
        if (!string.IsNullOrEmpty(chat.SystemPrompt))
            yield return OpenAI.Chat.ChatMessage.CreateSystemMessage(chat.SystemPrompt!);

        var promptText = StripInlineImageMarkdown(currentMessage);

        var ragContext = GetRagContext(chat, promptText);
        if (!string.IsNullOrEmpty(ragContext))
            yield return OpenAI.Chat.ChatMessage.CreateSystemMessage(
                $"Relevant context from uploaded documents:\n{ragContext}\n\nUse the above context to inform your response.");

        var history = chat.Messages
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .OrderBy(m => m.CreatedAt)
            .ToList();

        // The last user message in history is the one we're about to send.
        // If we have attachments, we'll re-emit it as a multimodal message
        // below, so skip it here to avoid duplicating it.
        var imageAttachments = currentAttachments?
            .Where(a => a.IsImage)
            .ToList() ?? new List<ChatAttachment>();
        var hasImages = imageAttachments.Count > 0;
        var skipIndex = hasImages && !skipLastUser
            ? history.FindLastIndex(m => m.Role == "user")
            : -1;

        for (var i = 0; i < history.Count; i++)
        {
            if (i == skipIndex)
                continue;

            var msg = history[i];
            // Strip inline data-URL image markdown from historical user messages.
            // The bytes are useful only on the most recent turn; replaying them
            // would balloon the request size and waste tokens.
            var content = msg.Role == "user" ? StripInlineImageMarkdown(msg.Content) : msg.Content;

            yield return msg.Role switch
            {
                "user" => OpenAI.Chat.ChatMessage.CreateUserMessage(content),
                "assistant" => OpenAI.Chat.ChatMessage.CreateAssistantMessage(content),
                _ => OpenAI.Chat.ChatMessage.CreateSystemMessage(content)
            };
        }

        if (skipLastUser)
            yield break;

        if (hasImages)
        {
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(promptText)
            };

            foreach (var image in imageAttachments)
            {
                var data = BinaryData.FromBytes(image.Content);
                var mediaType = string.IsNullOrWhiteSpace(image.ContentType)
                    ? "image/png"
                    : image.ContentType;
                parts.Add(ChatMessageContentPart.CreateImagePart(data, mediaType));
            }

            yield return OpenAI.Chat.ChatMessage.CreateUserMessage(parts);
        }
        else
        {
            yield return OpenAI.Chat.ChatMessage.CreateUserMessage(promptText);
        }
    }

    // Matches ![alt](data:<mime>;base64,...) markdown image references.
    private static readonly System.Text.RegularExpressions.Regex InlineImageRegex =
        new(@"!\[[^\]]*\]\(data:[^\)]+\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripInlineImageMarkdown(string content)
    {
        if (string.IsNullOrEmpty(content) || !content.Contains("](data:", StringComparison.Ordinal))
            return content;

        var stripped = InlineImageRegex.Replace(content, string.Empty).Trim();
        return stripped.Length == 0 ? "[image]" : stripped;
    }

    private static string GetRagContext(Chat chat, string query)
    {
        var queryTerms = query.ToLowerInvariant()
            .Split(new char[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '|' }, StringSplitOptions.RemoveEmptyEntries);

        var chunks = chat.DocumentChunks
            .Where(c => queryTerms.Any(t => c.Text.ToLowerInvariant().Contains(t)))
            .OrderByDescending(c => c.ChunkSize)
            .Take(5)
            .ToList();

        if (!chunks.Any())
            return string.Empty;

        return string.Join("\n---\n", chunks.Select(c =>
            $"From {c.FileName} (chunk {c.ChunkIndex}):\n{c.Text}"));
    }

    private static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Azure:Endpoint is not a valid absolute URI.");

        var builder = new UriBuilder(uri);

        if (builder.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
            && builder.Path.StartsWith("/api/projects/", StringComparison.OrdinalIgnoreCase))
        {
            builder.Path = "/openai/v1/";
            builder.Query = string.Empty;
            return builder.Uri;
        }

        var path = builder.Path.TrimEnd('/');

        if (!path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = string.IsNullOrWhiteSpace(path) || path == "/"
                ? "/openai/v1"
                : $"{path}/openai/v1";
        }

        builder.Path = $"{path.TrimEnd('/')}/";
        return builder.Uri;
    }
}
