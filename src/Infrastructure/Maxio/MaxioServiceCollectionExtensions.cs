using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration: binds <see cref="MaxioSettings"/> from the "Maxio"
    /// configuration section, configures a typed <see cref="System.Net.Http.HttpClient"/> with the spec's base
    /// address and HTTP Basic auth, and registers the billing service.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.SectionName));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((serviceProvider, httpClient) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            ValidateSettings(settings);

            httpClient.BaseAddress = settings.ResolveBaseAddress();

            // HTTP Basic: username = API key, password = literal "x" (per the spec's BasicAuth scheme).
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();

        return services;
    }

    private static void ValidateSettings(MaxioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment configuration.");
        }
    }
}
