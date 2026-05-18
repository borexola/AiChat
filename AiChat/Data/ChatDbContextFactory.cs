using AiChat.Config;
using AiChat.Data.Encryption;
using Azure.Identity;
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
            Url = configuration.GetValue<string>("KeyVault:Url"),
            UseManagedIdentity = configuration.GetValue<bool>("KeyVault:UseManagedIdentity")
        };

        var encryptionConfig = new EncryptionConfig
        {
            KeyName = configuration.GetValue<string>("Encryption:KeyName")
        };

        // Build the same credential strategy used at runtime so migrations can
        // reach Key Vault if needed. Design-time never writes data, so this is
        // only exercised when EF scaffolds snapshots that touch encrypted columns.
        Azure.Core.TokenCredential credential = keyVaultConfig.UseManagedIdentity
            ? new DefaultAzureCredential()
            : new ClientSecretCredential(
                configuration.GetValue<string>("EntraId:TenantId") ?? string.Empty,
                configuration.GetValue<string>("EntraId:ClientId") ?? string.Empty,
                configuration.GetValue<string>("EntraId:ClientSecret") ?? string.Empty);

        var keyManager = new KeyManager(keyVaultConfig, encryptionConfig, credential);
        return new ChatDbContext(optionsBuilder.Options, keyManager);
    }
}
