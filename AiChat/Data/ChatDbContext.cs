using AiChat.Data.Encryption;
using AiChat.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChat.Data;

public class ChatDbContext : DbContext
{
    private readonly IEncryptionKeyManager _keyManager;

    public ChatDbContext(DbContextOptions<ChatDbContext> options, IEncryptionKeyManager keyManager)
        : base(options)
    {
        _keyManager = keyManager;
    }

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.SystemPrompt)
                .HasTypedConversion(_keyManager);

            entity.HasMany(e => e.Messages)
                .WithOne(e => e.Chat)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.DocumentChunks)
                .WithOne(e => e.Chat)
                .HasForeignKey(e => e.ChatId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChatId, e.CreatedAt });
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.Content)
                .HasTypedConversion(_keyManager)
                .HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChatId, e.CreatedAt });
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.Text)
                .HasTypedConversion(_keyManager)
                .HasColumnType("nvarchar(max)");
            entity.Property(e => e.Embedding)
                .HasConversion(new IntArrayToByteArrayConverter())
                .HasColumnType("varbinary(max)");
        });
    }
}
