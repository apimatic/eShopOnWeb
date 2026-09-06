using System;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing, bound from the "Maxio"
    /// configuration section.
    /// </summary>
    /// <remarks>
    /// Settings are validated at startup: a site that is missing its API key should fail to boot
    /// rather than serve traffic that fails one endpoint at a time.
    /// </remarks>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(ValidateBaseAddress, FormatBaseAddressError())
            .ValidateOnStart();

        services.AddMemoryCache();

        // One lock instance for the whole process, keyed by customer reference.
        services.AddSingleton<KeyedAsyncLock>();

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.BaseAddress = settings.ResolveBaseAddress();

                // Bounds a single attempt; the client layers its own retries on top.
                client.Timeout = settings.Timeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            // Recycle pooled connections so DNS changes on the Maxio side are picked up.
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    private static bool ValidateBaseAddress(MaxioSettings settings)
    {
        try
        {
            settings.ResolveBaseAddress();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FormatBaseAddressError() =>
        $"{MaxioSettings.SectionName}:BaseUrl must be an absolute URI when supplied, and " +
        $"{MaxioSettings.SectionName}:Subdomain must be set otherwise. Configure them with " +
        "user-secrets (dotnet user-secrets set \"Maxio:Subdomain\" ...) or the Maxio__Subdomain " +
        "environment variable - never in a checked-in settings file.";
}
