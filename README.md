# AiChat

A Blazor Server chat application that uses Azure OpenAI models with Azure Entra ID authentication and per-conversation document context (RAG).

## Features

- **Azure Entra ID authentication** — sign-in via Microsoft identity platform (OIDC); no local accounts
- **Multiple Azure OpenAI deployments** — select from any number of configured models per conversation
- **Streaming responses** — assistant replies stream token-by-token
- **Conversation history** — persisted to SQL Server with AES-256-GCM encryption at rest
- **Document upload (RAG)** — attach PDF, Word, plain text, image, and other files; relevant chunks are injected into the prompt automatically
- **Image attachments** — paste or upload images as part of a message (multimodal)
- **Message editing & regeneration** — edit past messages or regenerate the last assistant reply
- **Chat title generation** — titles are generated automatically from the first message

## Architecture

| Layer | Technology |
|---|---|
| Frontend | Blazor Server (.NET 10) |
| Auth | Microsoft.Identity.Web / Azure Entra ID (OIDC) |
| AI | Azure OpenAI (via `OpenAI` .NET SDK) |
| Database | SQL Server / EF Core (code-first migrations) |
| Secrets | Azure Key Vault (all runtime secrets) |
| Encryption | AES-256-GCM (applied to sensitive DB columns) |

## Prerequisites

- .NET 10 SDK
- SQL Server (local or Azure SQL)
- An **Azure AI Foundry** project with at least one model deployment (create one at [ai.azure.com](https://ai.azure.com))
- An **Azure Entra ID** app registration (for authentication)
- An **Azure Key Vault** (for secrets at runtime)

## Configuration

All secrets must be stored in Azure Key Vault. The app loads them at startup using `DefaultAzureCredential`. Secret names use `--` as the section separator (e.g. `Azure--ApiKey` maps to `Azure:ApiKey`).

### Key Vault secrets

| Secret name | Maps to | Description |
|---|---|---|
| `Azure--Endpoint` | `Azure:Endpoint` | Azure OpenAI resource endpoint, e.g. `https://<resource>.openai.azure.com/` |
| `Azure--ApiKey` | `Azure:ApiKey` | Azure OpenAI API key |
| `EntraId--TenantId` | `EntraId:TenantId` | Entra ID tenant GUID |
| `EntraId--ClientId` | `EntraId:ClientId` | Entra ID app registration client ID |
| `EntraId--ClientSecret` | `EntraId:ClientSecret` | Entra ID client secret |
| `Sql--ConnectionString` | `Sql:ConnectionString` | SQL Server connection string |
| `encryption-key` | *(managed by KeyManager)* | 32-byte base64 key used for AES-256-GCM column encryption |

> **Important:** Never put real secrets in `appsettings.json` or `appsettings.Development.json`. Both files are checked into source control (Development overrides are gitignored by default — see `.gitignore`).

### `appsettings.json` (non-secret config)

```json
{
  "KeyVault": {
    "Url": "https://<your-keyvault>.vault.azure.net/"
  },
  "Azure": {
    "DefaultDeployment": "gpt-4o",
    "Deployments": [
      { "Id": "gpt-4o", "DisplayName": "GPT-4o" }
    ]
  },
  "EntraId": {
    "Instance": "https://login.microsoftonline.com",
    "CallbackPath": "/signin-oidc"
  },
  "Encryption": {
    "KeyName": "encryption-key"
  }
}
```

**`Azure:DefaultDeployment`** is the default deployment used when none is explicitly selected and as a fallback. **`Azure:Deployments`** defines the full list of deployments shown in the UI. If `Deployments` is empty the app falls back to a single entry using `DefaultDeployment`.

### Local development

Copy `appsettings.Development.json` from the template below and populate values for your local environment. This file is gitignored and should never be committed.

```json
{
  "KeyVault": {
    "Url": "https://<your-dev-keyvault>.vault.azure.net/"
  },
  "Azure": {
    "Endpoint": null,
    "ApiKey": null
  },
  "EntraId": {
    "Instance": "https://login.microsoftonline.com",
    "TenantId": null,
    "ClientId": null,
    "CallbackPath": "/signin-oidc"
  },
  "Sql": {
    "ConnectionString": "Server=.;Database=AiChat;TrustServerCertificate=true;Trusted_Connection=true;"
  },
  "Encryption": {
    "KeyName": "encryption-key"
  }
}
```

Secrets can be loaded from Key Vault (recommended) or overridden inline for local testing. Make sure the account running the app (`az login` / managed identity) has **Key Vault Secrets User** role on the vault.

## Getting started

```bash
# 1. Restore & build
dotnet build

# 2. Apply database migrations
dotnet ef database update --project AiChat

# 3. Run
dotnet run --project AiChat
```

## Planned improvements

- Support for additional AI provider backends (OpenAI direct, Anthropic, etc.)
- Additional identity provider support beyond Azure Entra ID
- Conversation sharing and export
- Streaming tool/function call support
