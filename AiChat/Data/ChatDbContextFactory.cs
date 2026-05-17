using AiChat.Config;
using AiChat.Data.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AiChat.Data;

public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetValue<string>("Sql:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Sql:ConnectionString is not configured for design-time DbContext creation.");

        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        var keyVaultConfig = new KeyVaultConfig
        {
            Url = configuration.GetValue<string>("KeyVault:Url")
        };

        var encryptionConfig = new EncryptionConfig
        {
            KeyName = configuration.GetValue<string>("Encryption:KeyName")
        };

        var keyManager = new KeyManager(keyVaultConfig, encryptionConfig);
        return new ChatDbContext(optionsBuilder.Options, keyManager);
    }
}
