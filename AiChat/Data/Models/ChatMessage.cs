using System.ComponentModel.DataAnnotations;

namespace AiChat.Data.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public string? ModelId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Chat? Chat { get; set; }
}
