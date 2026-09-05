using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the Maxio Advanced Billing integration. Binds <see cref="MaxioSettings"/> from
    /// the "Maxio" configuration section - populate it via user-secrets (Development) or
    /// Maxio__* environment variables (elsewhere); never hard-code values here, since the same
    /// build must be able to target a different Maxio site/catalog.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(settings => maxioSection.Bind(settings));
        services.AddSingleton<MaxioBuyerLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            var baseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                ? settings.BaseUrl!
                : !string.IsNullOrWhiteSpace(settings.Subdomain)
                    ? $"https://{settings.Subdomain}.chargify.com"
                    : throw new InvalidOperationException("Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");

            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException("Maxio is not configured: Maxio:ApiKey is required.");
            }

            // Maxio Advanced Billing uses HTTP Basic Auth: the API key as the username, the
            // literal string "x" as the password.
            var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }
}
