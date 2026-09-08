using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Maxio Advanced Billing integration from the "Maxio" configuration
    /// section (Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle, Maxio:BaseUrl).
    /// Values are expected to come from user-secrets / environment variables — never from
    /// files committed to the repository. Fails fast at startup when required settings
    /// are missing.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION_NAME).Get<MaxioSettings>() ?? new MaxioSettings();

        // Fail fast: bad or missing configuration should surface at startup, not on first subscribe.
        settings.GetRequiredApiKey();
        var baseAddress = settings.GetApiBaseAddress();

        services.AddSingleton(settings);

        services.AddHttpClient<IMaxioBillingClient, MaxioBillingClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);

            // Maxio API auth: HTTP Basic with the API key as username and a literal "X" password.
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:X"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
