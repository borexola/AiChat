using System.ComponentModel.DataAnnotations;

namespace AiChat.Data.Models;

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChatId { get; set; }
    public required string FileName { get; set; }
    public required string Text { get; set; }
    public int ChunkIndex { get; set; }
    public int ChunkSize { get; set; }
    public int[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Chat? Chat { get; set; }
}
