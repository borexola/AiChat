using System.ComponentModel.DataAnnotations;

namespace AiChat.Data.Models;

public class Chat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? SystemPrompt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = [];
    public ICollection<DocumentChunk> DocumentChunks { get; set; } = [];
}
