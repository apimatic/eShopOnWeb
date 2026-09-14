using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named HttpClient the Maxio client is built on (kept off the shared default client).</summary>
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers Maxio Advanced Billing subscription billing. Requires a populated
    /// <c>Maxio</c> configuration section; when it is missing an <see cref="IMaxioSubscriptionService"/>
    /// that fails with a clear 503 is registered instead so the host still starts.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MaxioOptions.SectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        services.AddSingleton(options);

        if (!options.IsConfigured)
        {
            services.AddSingleton<IMaxioSubscriptionService, UnconfiguredMaxioSubscriptionService>();
            return services;
        }

        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                // Bounds a single HTTP attempt; the SDK retry layer has its own per-attempt
                // timeout and the overall call is budgeted in MaxioSubscriptionService.
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The Maxio client below is a singleton, so keep DNS/connection state fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler(() => new WriteOnceHttpMessageHandler());

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            var maxioOptions = sp.GetRequiredService<MaxioOptions>();
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(maxioOptions));
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = options.ApiKey!, Password = "x" },
            // The SDK default per-attempt timeout is 100s, which is far too long for an
            // interactive request path. Bounds one attempt; MaxioSubscriptionService bounds
            // the whole call with a CancellationToken.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            // Verbatim override (no {site} placeholder present -> used literally).
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }
        else
        {
            clientOptions.Server.Production.Us.Site = options.Subdomain!;
        }

        return clientOptions;
    }
}
