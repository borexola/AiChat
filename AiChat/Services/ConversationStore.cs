using AiChat.Data;
using AiChat.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChat.Services;

public class ConversationStore : IConversationStore
{
    private readonly IDbContextFactory<ChatDbContext> _contextFactory;

    public ConversationStore(IDbContextFactory<ChatDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Chat> CreateChatAsync(string userId, string? title = null, string? systemPrompt = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var chat = new Chat
        {
            UserId = userId,
            Title = title ?? "New Chat",
            SystemPrompt = systemPrompt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Chats.Add(chat);
        await context.SaveChangesAsync();
        return chat;
    }

    public async Task<Chat?> GetChatAsync(Guid id, string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Chats
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
    }

    public async Task<IEnumerable<Chat>> GetChatsAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Chats
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();
    }

    public async Task UpdateChatAsync(Chat chat)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        chat.UpdatedAt = DateTime.UtcNow;
        context.Chats.Update(chat);
        await context.SaveChangesAsync();
    }

    public async Task DeleteChatAsync(Guid id, string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var chat = await context.Chats.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (chat != null)
        {
            context.Chats.Remove(chat);
            await context.SaveChangesAsync();
        }
    }

    public async Task AddMessageAsync(ChatMessage message)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Messages.Add(message);
        await context.SaveChangesAsync();
    }

    public async Task DeleteMessageAsync(Guid messageId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var msg = await context.Messages.FindAsync(messageId);
        if (msg != null)
        {
            context.Messages.Remove(msg);
            await context.SaveChangesAsync();
        }
    }

    public async Task UpdateMessageAsync(Guid messageId, string newContent)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var msg = await context.Messages.FindAsync(messageId);
        if (msg != null)
        {
            msg.Content = newContent;
            await context.SaveChangesAsync();
        }
    }

    public async Task DeleteMessagesAfterAsync(Guid chatId, DateTime afterUtc)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var toDelete = await context.Messages
            .Where(m => m.ChatId == chatId && m.CreatedAt > afterUtc)
            .ToListAsync();
        if (toDelete.Count > 0)
        {
            context.Messages.RemoveRange(toDelete);
            await context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(Guid chatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Messages
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task SaveChunksAsync(IEnumerable<DocumentChunk> chunks)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.DocumentChunks.AddRange(chunks);
        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<DocumentChunk>> SearchChunksAsync(Guid chatId, string query, int topK = 5)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Simple keyword search across chunks
        var queryTerms = query.ToLowerInvariant().Split(new char[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '|' }, StringSplitOptions.RemoveEmptyEntries);

        var chunks = await context.DocumentChunks
            .Where(c => c.ChatId == chatId)
            .ToListAsync();

        var results = chunks
            .Select(c => new
            {
                Chunk = c,
                Score = queryTerms.Count(t => c.Text.ToLowerInvariant().Contains(t))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Chunk.ChunkSize)
            .Take(topK)
            .Select(x => x.Chunk)
            .ToList();

        return results;
    }
}
