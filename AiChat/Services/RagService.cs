using AiChat.Config;
using AiChat.Data;
using AiChat.Data.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using UglyToad.PdfPig;
using StringBuilder = System.Text.StringBuilder;

namespace AiChat.Services;

public class RagService : IRagService
{
    private readonly RagConfig _ragConfig;
    private readonly IDbContextFactory<ChatDbContext> _contextFactory;

    public RagService(IOptions<AppSettings> settings, IDbContextFactory<ChatDbContext> contextFactory)
    {
        _ragConfig = settings.Value.Rag;
        _contextFactory = contextFactory;
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".txt", ".md", ".json", ".csv",
        ".png", ".jpg", ".jpeg", ".gif"
    };

    public async Task<List<string>> ProcessAndChunkDocumentAsync(Guid chatId, string fileName, Stream fileStream, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Sanitize and validate the file name to prevent path traversal and unsupported types.
        var safeName = Path.GetFileName(fileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new NotSupportedException($"File type '{extension}' is not supported.");

        var text = extension switch
        {
            ".pdf" => ExtractTextFromPdf(fileStream),
            ".docx" => await ExtractTextFromDocx(fileStream, ct),
            ".txt" or ".md" or ".json" or ".csv" => await ExtractTextFromText(fileStream, ct),
            ".png" or ".jpg" or ".jpeg" or ".gif" => await ExtractImageContextAsync(safeName, fileStream, ct),
            _ => throw new NotSupportedException($"File type '{extension}' is not supported.")
        };

        var chunks = ChunkText(text);

        // Save chunks with embedding metadata
        var savedChunks = new List<string>();
        foreach (var chunk in chunks)
        {
            var chunkModel = new DocumentChunk
            {
                ChatId = chatId,
                FileName = safeName,
                Text = chunk.Text,
                ChunkIndex = chunk.Index,
                ChunkSize = chunk.Text.Length,
                Embedding = GenerateSimpleEmbedding(chunk.Text)
            };
            savedChunks.Add(chunk.Text);
            context.DocumentChunks.Add(chunkModel);
        }
        await context.SaveChangesAsync(ct);

        return savedChunks;
    }

    private string ExtractTextFromPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.Append(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<string> ExtractTextFromDocx(Stream stream, CancellationToken ct)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var sb = new StringBuilder();
        var body = document.MainDocumentPart?.Document.Body;
        foreach (var para in body?.Elements<Paragraph>() ?? [])
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(para.InnerText);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<string> ExtractTextFromText(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> ExtractImageContextAsync(string fileName, Stream stream, CancellationToken ct)
    {
        // The incoming Blazor browser stream is forward-only and chunked over SignalR.
        // Copy it into memory first so the image decoder can seek and so a decoder
        // failure does not propagate as an unhandled exception in the circuit.
        await using var bufferedStream = new MemoryStream();
        await stream.CopyToAsync(bufferedStream, ct);
        bufferedStream.Position = 0;

        var sb = new StringBuilder();
        sb.AppendLine($"File: {fileName}");
        sb.AppendLine($"Size: {bufferedStream.Length} bytes");

        try
        {
            bufferedStream.Position = 0;
            var info = await Image.IdentifyAsync(bufferedStream, ct);
            if (info is not null)
            {
                sb.AppendLine($"Dimensions: {info.Width}x{info.Height}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sb.AppendLine($"Note: image metadata could not be read ({ex.GetType().Name}).");
        }

        sb.AppendLine("Pixel data available for AI vision models");
        return sb.ToString();
    }

    private List<(string Text, int Index)> ChunkText(string text)
    {
        var chunks = new List<(string Text, int Index)>();
        var sentences = SplitIntoSentences(text);

        if (sentences.Count <= 1)
        {
            chunks.Add((text.Trim(), 0));
            return chunks;
        }

        var currentChunk = new StringBuilder();
        var index = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > _ragConfig.MaxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add((currentChunk.ToString().Trim(), index++));
                // Keep last N characters as overlap for context continuity
                var overlapText = currentChunk.ToString().Substring(Math.Max(0, currentChunk.Length - _ragConfig.ChunkOverlap));
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }
            currentChunk.Append(sentence);
        }

        if (currentChunk.Length > 0)
            chunks.Add((currentChunk.ToString().Trim(), index));

        return chunks;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();

        foreach (var c in text)
        {
            current.Append(c);
            if (c is '.' or '!' or '?')
            {
                var sentence = current.ToString().Trim();
                if (!string.IsNullOrEmpty(sentence))
                    sentences.Add(sentence);
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            var remaining = current.ToString().Trim();
            if (!string.IsNullOrEmpty(remaining))
                sentences.Add(remaining);
        }

        return sentences;
    }

    private int[] GenerateSimpleEmbedding(string text)
    {
        // Simple TF-IDF-like embedding using character n-gram frequency
        const int dim = 64;
        var embedding = new int[dim];

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var hash = Math.Abs(c.GetHashCode()) % dim;
            embedding[hash] += 1;
        }

        // Normalize
        var max = embedding.Max() > 0 ? embedding.Max() : 1;
        return embedding.Select(e => e / max).ToArray();
    }
}
