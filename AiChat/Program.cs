using AiChat.Auth;
using AiChat.Components;
using AiChat.Config;
using AiChat.Data;
using AiChat.Services;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Explicitly load environment variables with the AICHAT_ prefix (highest local priority).
// Standard unprefixed env vars (e.g. EntraId__ClientSecret) are already loaded by the
// default builder. Prefixed vars are stripped of the prefix before mapping, so
// AICHAT_EntraId__ClientSecret → EntraId:ClientSecret in configuration.
builder.Configuration.AddEnvironmentVariables("AICHAT_");

// If KeyVault URL is configured, load all secrets from KeyVault first
// Secret naming convention: Azure--ApiKey, Sql--ConnectionString, EntraId--TenantId, etc.
// Resolved Key Vault credential — also used by KeyManager for the encryption key.
TokenCredential? resolvedKvCredential = null;

var kvUrl = builder.Configuration["KeyVault:Url"];
if (!string.IsNullOrEmpty(kvUrl))
{
    var useManagedIdentity = builder.Configuration.GetValue<bool>("KeyVault:UseManagedIdentity");

    TokenCredential kvCredential;
    if (useManagedIdentity)
    {
        kvCredential = new DefaultAzureCredential();
    }
    else
    {
        var tenantId     = builder.Configuration["EntraId:TenantId"]     ?? throw new InvalidOperationException("EntraId:TenantId is required for Key Vault access when not using managed identity.");
        var clientId     = builder.Configuration["EntraId:ClientId"]     ?? throw new InvalidOperationException("EntraId:ClientId is required for Key Vault access when not using managed identity.");
        var clientSecret = builder.Configuration["EntraId:ClientSecret"] ?? throw new InvalidOperationException("EntraId:ClientSecret is required for Key Vault access when not using managed identity.");
        kvCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    }

    resolvedKvCredential = kvCredential;
    var client = new SecretClient(new Uri(kvUrl), kvCredential);
    var secretValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    var secretList = new List<Azure.Security.KeyVault.Secrets.SecretProperties>();
    await foreach (var prop in client.GetPropertiesOfSecretsAsync())
        secretList.Add(prop);

    foreach (var secret in secretList)
    {
        if (secret.Name.Equals("encryption-key", StringComparison.OrdinalIgnoreCase))
            continue; // Handled separately by KeyManager

        var secretValue = client.GetSecret(secret.Name).Value.Value;
        var configKey = secret.Name
            .Replace("--", ":")
            .Replace("__", ":");

        // Local config (appsettings.Development.json, env vars, etc.) takes
        // priority over Key Vault so developers can override values without
        // touching the vault.
        if (!string.IsNullOrEmpty(builder.Configuration[configKey]))
            continue;

        secretValues[configKey] = secretValue;
    }

    if (secretValues.Count > 0)
        builder.Configuration.AddInMemoryCollection(secretValues);
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Required so the Blazor circuit accepts file upload chunks and large
        // JS interop payloads (pasted images sent as base64 data URLs).
        options.MaximumReceiveMessageSize = 32 * 1024 * 1024;
    });

builder.Services.AddControllers();
builder.Services.AddCascadingAuthenticationState();

// HttpContext accessor for Blazor Server
builder.Services.AddHttpContextAccessor();

// Configuration
builder.Services.Configure<KeyVaultConfig>(builder.Configuration.GetSection("KeyVault"));
builder.Services.Configure<AppSettings>(builder.Configuration);

// Authentication
builder.AddAzureEntraAuth();

// Database
var sqlConn = builder.Configuration.GetSection("Sql").GetValue<string>("ConnectionString")
    ?? throw new InvalidOperationException("Sql:ConnectionString is not configured.");
builder.Services.AddDbContextFactory<ChatDbContext>(options =>
    options.UseSqlServer(sqlConn));

// Services
builder.Services.AddScoped<IConversationStore, ConversationStore>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddSingleton<IEncryptionKeyManager>(sp =>
{
    var kvConfig = builder.Configuration.GetSection("KeyVault").Get<KeyVaultConfig>()
        ?? new KeyVaultConfig();
    var encConfig = builder.Configuration.GetSection("Encryption").Get<EncryptionConfig>()
        ?? new EncryptionConfig();
    return new KeyManager(kvConfig, encConfig, resolvedKvCredential);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/account/signin", (string? returnUrl, HttpContext httpContext) =>
{
    // Reject absolute/external URLs to prevent open redirect attacks.
    var redirectUri = (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        ? returnUrl
        : "/";

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = redirectUri },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapGet("/account/signout", (string? returnUrl) =>
{
    // Reject absolute/external URLs to prevent open redirect attacks.
    var redirectUri = (!string.IsNullOrWhiteSpace(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
        ? returnUrl
        : "/";

    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = redirectUri },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map Microsoft.Identity.Web auth endpoints
app.MapControllers();

app.Run();
