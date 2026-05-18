namespace AiChat.Config;

public class KeyVaultConfig
{
    public string? Url { get; set; }

    /// <summary>
    /// When true, uses <see cref="Azure.Identity.ManagedIdentityCredential"/> to
    /// authenticate with Key Vault (suitable for Azure VMs, App Service, AKS, etc.).
    /// When false (default), falls back to the Entra ID app registration credentials
    /// configured in <c>EntraId:ClientId</c> / <c>EntraId:ClientSecret</c> /
    /// <c>EntraId:TenantId</c>.
    /// </summary>
    public bool UseManagedIdentity { get; set; } = false;
}
