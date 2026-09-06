using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK runs on.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers Maxio Advanced Billing as the subscription billing system of record.
    /// <para>
    /// The SDK's own DI extension is deliberately not used: it resolves the default, unnamed
    /// <see cref="IHttpClientFactory"/> client, which is shared with every other unnamed consumer in the
    /// application — the timeout and the write-once handler this integration needs would leak onto all of
    /// them. Registering by hand keeps that pipeline scoped to Maxio.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        services.AddMemoryCache();
        services.AddTransient<MaxioCallTrackingHandler>();

        services.AddHttpClient(HttpClientName, (serviceProvider, httpClient) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

                // Bounds a single attempt. Without it the default is 100s, which turns a hung provider into
                // an outage. The whole-call budget is enforced separately, by cancellation token.
                httpClient.Timeout = options.AttemptTimeout;
            })
            .AddHttpMessageHandler<MaxioCallTrackingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so it holds one HttpClient for the process lifetime
                // and never picks up the factory's handler rotation. This keeps DNS from being cached forever.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));
        });

        services.AddSingleton<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            // Maxio authenticates with the API key as the HTTP Basic username and the literal "x" as password.
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey ?? string.Empty,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = Math.Max(1, options.MaxRetries),
                Timeout = options.AttemptTimeout
            }
        };

        // The environment selects which per-region server options are read; the base URL on that region
        // selects the host. Both must be set consistently, so they are configured together here.
        // There is no sandbox environment value — a sandbox site is selected by its subdomain.
        var subdomain = string.IsNullOrWhiteSpace(options.Subdomain) ? null : options.Subdomain!.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? null : options.BaseUrl!.Trim();

        if (options.IsEuropeanRegion)
        {
            clientOptions.Environment = ServerEnvironment.Eu;

            if (subdomain is not null)
            {
                clientOptions.Server.Production.Eu.Site = subdomain;
            }

            if (baseUrl is not null)
            {
                clientOptions.Server.Production.Eu.BaseUrl = baseUrl;
            }
        }
        else
        {
            clientOptions.Environment = ServerEnvironment.Us;

            if (subdomain is not null)
            {
                clientOptions.Server.Production.Us.Site = subdomain;
            }

            if (baseUrl is not null)
            {
                clientOptions.Server.Production.Us.BaseUrl = baseUrl;
            }
        }

        return clientOptions;
    }
}
