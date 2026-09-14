using System;
using System.Collections.Generic;
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
    public const string HttpClientName = "Maxio.AdvancedBilling";

    /// <summary>
    /// Adds the <c>Maxio:</c> values that arrive as environment variables (MAXIO_API_KEY,
    /// MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY) into the configuration as
    /// <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c> and <c>Maxio:ProductFamilyHandle</c>
    /// respectively. Empty variables are skipped so other configuration sources (e.g. .NET
    /// user secrets) can supply the value. Only the environment variable names are referenced;
    /// values are read at runtime and never written to the repository.
    /// </summary>
    public static IConfigurationBuilder AddMaxioEnvironmentVariables(this IConfigurationBuilder builder)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddEnvironmentValue("MAXIO_API_KEY", MaxioOptions.CONFIG_NAME, nameof(MaxioOptions.ApiKey));
        AddEnvironmentValue("MAXIO_SITE_SUBDOMAIN", MaxioOptions.CONFIG_NAME, nameof(MaxioOptions.Subdomain));
        AddEnvironmentValue("MAXIO_DEFAULT_PRODUCT_FAMILY", MaxioOptions.CONFIG_NAME, nameof(MaxioOptions.ProductFamilyHandle));
        return builder.AddInMemoryCollection(values);

        void AddEnvironmentValue(string environmentVariable, string section, string key)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[$"{section}:{key}"] = value;
            }
        }
    }

    /// <summary>
    /// Registers the Maxio Advanced Billing SDK client (single long-lived instance over a
    /// named, pooled <see cref="HttpClient"/>) and the subscription service that fronts it.
    /// Settings are bound from the <c>Maxio:</c> configuration section.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.CONFIG_NAME));

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop per attempt. The SDK's own retry timeout is set in the options
                // below; this bounds a single hung socket at ~30s.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // DNS/connection reuse must stay fresh behind the long-lived SDK client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxioOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            var clientOptions = BuildClientOptions(maxioOptions);
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions
                    {
                        Site = string.IsNullOrWhiteSpace(options.Subdomain) ? null : options.Subdomain,
                        // Optional override used verbatim; otherwise derive from the subdomain.
                        BaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                            ? "https://{site}.chargify.com"
                            : options.BaseUrl
                    }
                }
            },
            // Keep interactive subscribe/list calls snappy: cap attempts and bound each
            // attempt. The whole call is additionally budgeted inside the service.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(20)
            }
        };

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
