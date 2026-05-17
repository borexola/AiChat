namespace AiChat.Services;

public interface IRagService
{
    Task<List<string>> ProcessAndChunkDocumentAsync(Guid chatId, string fileName, Stream fileStream, CancellationToken ct);
}
