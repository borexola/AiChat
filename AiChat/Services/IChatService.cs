namespace AiChat.Services;

public interface IChatService
{
    Task<Guid> StartChatAsync(string userId, string message, string? systemPrompt = null);
    IAsyncEnumerable<string> SendMessageAsync(Guid chatId, string userMessage, CancellationToken ct, string? modelId = null);
    IAsyncEnumerable<string> SendMessageAsync(Guid chatId, string userMessage, IReadOnlyList<ChatAttachment> attachments, CancellationToken ct, string? modelId = null);
    IAsyncEnumerable<string> RegenerateAsync(Guid chatId, CancellationToken ct, string? modelId = null);
    IAsyncEnumerable<string> RespondToCurrentHistoryAsync(Guid chatId, CancellationToken ct, string? modelId = null);
    IReadOnlyList<AiChat.Config.ModelOption> GetAvailableModels();
    string DefaultModelId { get; }
    Task<string> GenerateTitleAsync(string firstUserMessage, CancellationToken ct);
}

/// <summary>
/// Represents a file the user attached to a single chat turn. The bytes are
/// passed in-memory because they originate from the Blazor circuit and must not
/// be re-read from the browser after the upload event completes.
/// </summary>
public sealed record ChatAttachment(string FileName, string ContentType, byte[] Content)
{
    public bool IsImage =>
        ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
