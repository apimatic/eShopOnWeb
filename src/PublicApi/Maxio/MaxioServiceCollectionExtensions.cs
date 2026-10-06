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
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.CONFIG_NAME);
        services.Configure<MaxioOptions>(section);

        services.AddHttpClient(MaxioHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var options = section.Get<MaxioOptions>() ?? new MaxioOptions();
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Maxio is not configured. Set the 'Maxio:ApiKey' configuration value (e.g. via user-secrets from the MAXIO_API_KEY environment variable).");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var environment = ResolveEnvironment();
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey,
                Password = "x"
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Eu.BaseUrl = options.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Eu.Site = options.Subdomain;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = options.Subdomain;
            }
        }

        return clientOptions;
    }

    private static ServerEnvironment ResolveEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        return string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;
    }
}
