using AiChat.Config;
using AiChat.Data.Encryption;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace AiChat.Auth;

public static class AzureEntraAuthExtensions
{
    public static WebApplicationBuilder AddAzureEntraAuth(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddMicrosoftIdentityWebApp(
            builder.Configuration.GetSection("EntraId"));

        builder.Services.AddAuthorization();

        return builder;
    }
}
