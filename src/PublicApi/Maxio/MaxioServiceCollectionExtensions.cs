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
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing SDK client and the <see cref="MaxioSubscriptionService"/>
    /// boundary. The SDK client is registered as a singleton over a named, factory-managed HttpClient
    /// so its handler pipeline (single-send guard, optional traffic logging, DNS-fresh primary handler)
    /// never leaks onto the app's shared default client. Credentials and target site are read from the
    /// Maxio configuration section / MAXIO_* environment variables; no secret value is hard-coded.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var maxioOptions = MaxioOptions.Load(configuration);
        services.AddSingleton(maxioOptions);

        var httpClientBuilder = services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop: the SDK's own retry pipeline sits above SendAsync, so a hung
                // provider is bounded per attempt by this value (the SDK default is 100s).
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton that holds its HttpClient for the process lifetime,
                // so keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler(() => new MaxioSingleSendHandler());

        if (maxioOptions.LogTraffic)
        {
            services.AddTransient<MaxioLoggingHandler>();
            httpClientBuilder.AddHttpMessageHandler<MaxioLoggingHandler>();
        }

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<MaxioOptions>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var sdkOptions = BuildSdkOptions(options);
            return new MaxioAdvancedBillingClient(httpClient, sdkOptions);
        });

        services.AddSingleton<MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildSdkOptions(MaxioOptions maxio)
    {
        if (string.IsNullOrWhiteSpace(maxio.ApiKey))
        {
            throw new MaxioConfigurationException("Maxio API key is not configured. Set Maxio:ApiKey (or MAXIO_API_KEY).");
        }

        if (string.IsNullOrWhiteSpace(maxio.BaseUrl) && string.IsNullOrWhiteSpace(maxio.Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio site is not configured. Set Maxio:Subdomain (or MAXIO_SITE_SUBDOMAIN), or provide Maxio:BaseUrl to override the site address.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                // Per attempt; the whole call is additionally bounded by the service's call budget.
                Timeout = TimeSpan.FromSeconds(10)
            }
        };

        var isEu = string.Equals(maxio.Environment, "eu", StringComparison.OrdinalIgnoreCase);

        if (isEu)
        {
            options.Environment = ServerEnvironment.Eu;
            options.Server.Production.Eu.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = maxio.BaseUrl;
            }
        }
        else
        {
            options.Environment = ServerEnvironment.Us;
            options.Server.Production.Us.Site = maxio.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
            }
        }

        return options;
    }
}
