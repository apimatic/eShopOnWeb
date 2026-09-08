using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioBillingServiceCollectionExtensions
{
    public const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.CONFIG_SECTION_NAME));

        services.AddHttpClient(MaxioHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(10)
            },
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions()
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }
        else if (!string.IsNullOrWhiteSpace(options.Subdomain))
        {
            clientOptions.Server.Production.Us.Site = options.Subdomain;
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            clientOptions.BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey,
                Password = "x"
            };
        }

        return clientOptions;
    }
}
