using AiChat.Data.Models;

namespace AiChat.Services;

public interface IConversationStore
{
    Task<Chat> CreateChatAsync(string userId, string? title = null, string? systemPrompt = null);
    Task<Chat?> GetChatAsync(Guid id);
    Task<IEnumerable<Chat>> GetChatsAsync(string userId);
    Task UpdateChatAsync(Chat chat);
    Task DeleteChatAsync(Guid id);
    Task AddMessageAsync(ChatMessage message);
    Task DeleteMessageAsync(Guid messageId);
    Task UpdateMessageAsync(Guid messageId, string newContent);
    Task DeleteMessagesAfterAsync(Guid chatId, DateTime afterUtc);
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(Guid chatId);
    Task SaveChunksAsync(IEnumerable<DocumentChunk> chunks);
    Task<IEnumerable<DocumentChunk>> SearchChunksAsync(Guid chatId, string query, int topK = 5);
}
