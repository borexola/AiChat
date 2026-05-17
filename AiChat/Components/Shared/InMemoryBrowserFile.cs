using Microsoft.AspNetCore.Components.Forms;

namespace AiChat.Components.Shared;

/// <summary>
/// In-memory <see cref="IBrowserFile"/> implementation. Used when the underlying
/// browser file stream may no longer be available (for example after the source
/// <see cref="InputFile"/> component has been re-rendered or unmounted), or when
/// the file originated from a non-InputFile source such as a clipboard paste.
/// </summary>
internal sealed class InMemoryBrowserFile(string name, string contentType, byte[] content) : IBrowserFile
{
    public string Name { get; } = name;
    public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
    public long Size { get; } = content.LongLength;
    public string ContentType { get; } = contentType;

    public Stream OpenReadStream(long maxAllowedSize = 25 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
            throw new IOException($"File size {Size} exceeds the allowed limit of {maxAllowedSize} bytes.");

        return new MemoryStream(content, writable: false);
    }
}
