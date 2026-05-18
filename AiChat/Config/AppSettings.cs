namespace AiChat.Config;

public class AppSettings
{
    public AzureConfig Azure { get; set; } = new();
    public EntraIdConfig EntraId { get; set; } = new();
    public SqlConfig Sql { get; set; } = new();
    public EncryptionConfig Encryption { get; set; } = new();
    public RagConfig Rag { get; set; } = new();
}

public class AzureConfig
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DefaultDeployment { get; set; }
    public List<DeploymentOption> Deployments { get; set; } = new();
}

public class DeploymentOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class EntraIdConfig
{
    public string? Instance { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CallbackPath { get; set; }
    public string? ClientCertificateThumbprint { get; set; }
}

public class SqlConfig
{
    public string? ConnectionString { get; set; }
}

public class EncryptionConfig
{
    public string? KeyName { get; set; }
}

public class RagConfig
{
    public int MaxChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 64;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
    public double MaxFileSizeMb { get; set; } = 10;
}
