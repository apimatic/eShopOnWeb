using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio billing integration: binds <see cref="MaxioSettings"/> from the
    /// <c>Maxio</c> configuration section and wires <see cref="IMaxioBillingService"/> as a typed
    /// <see cref="System.Net.Http.HttpClient"/> pre-configured with the API base address and
    /// Basic authentication.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        // Resolve a validated settings singleton so misconfiguration fails fast and clearly.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            ValidateRequired(settings.ApiKey, MaxioSettings.SectionName + ":ApiKey");
            ValidateRequired(settings.Subdomain, MaxioSettings.SectionName + ":Subdomain");
            ValidateRequired(settings.ProductFamilyHandle, MaxioSettings.SectionName + ":ProductFamilyHandle");
            return settings;
        });

        services.AddHttpClient<IMaxioBillingService, MaxioBillingService>((sp, client) =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            client.BaseAddress = settings.ResolveBaseAddress();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio uses HTTP Basic auth with the API key as the username and "X" as the password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            // Maxio enforces a 120s request cut-off; keep the client timeout aligned.
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        return services;
    }

    private static void ValidateRequired(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new MaxioBillingException(
                MaxioBillingErrorKind.Configuration,
                $"Maxio configuration value '{key}' is missing. Provide it via user-secrets or environment configuration.");
        }
    }
}
