using System;
using System.Net.Http;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio Advanced Billing: binds the "Maxio" options, constructs a single shared
    /// <see cref="AdvancedBillingClient"/>, and exposes <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey must be configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Subdomain), "Maxio:Subdomain must be configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle must be configured.");

        // One client instance is safe to share for the whole app.
        services.AddSingleton(sp => BuildClient(sp.GetRequiredService<IOptions<MaxioOptions>>().Value));

        // Per-subscriber serialization for idempotent subscribe must be shared across requests.
        services.AddSingleton<KeyedAsyncLock>();

        services.AddScoped<ISubscriptionBillingService, MaxioBillingService>();

        return services;
    }

    private static AdvancedBillingClient BuildClient(MaxioOptions options)
    {
        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(new BasicAuthModel.Builder(options.ApiKey, "x").Build())
            .Environment(ParseEnvironment(options.Environment))
            .Site(options.Subdomain);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            // Honor the verbatim base-address override: route every SDK request through a handler
            // that rewrites the host/scheme/path prefix to the configured URL.
            var httpClient = new HttpClient(new BaseAddressRewriteHandler(options.BaseUrl!)
            {
                InnerHandler = new HttpClientHandler()
            });
            builder.HttpClientConfig(config => config.HttpClientInstance(httpClient));
        }

        return builder.Build();
    }

    private static AdvancedBilling.Standard.Environment ParseEnvironment(string? environment) =>
        string.Equals(environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? AdvancedBilling.Standard.Environment.EU
            : AdvancedBilling.Standard.Environment.US;
}
