using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named <see cref="HttpClient"/> used only by the Maxio client (kept off the shared default client).</summary>
    public const string HttpClientName = "Maxio.AdvancedBilling";

    /// <summary>Per-attempt timeout applied by <see cref="HttpClient"/>. Bounds a single stalled socket.</summary>
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(25);

    /// <summary>Per-attempt timeout applied by the SDK retry pipeline.</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long a pooled connection is reused before a DNS refresh.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddMaxioSubscriptionServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<MaxioOptions>>().Value);

        services.AddHttpContextAccessor();

        services.AddTransient<MaxioSingleSendHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = HttpClientTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = PooledConnectionLifetime,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            })
            .AddHttpMessageHandler<MaxioSingleSendHandler>();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(configuration, options));
        });

        services.AddSingleton<MaxioSubscriptionService>();
        services.AddSingleton<IMaxioSubscriptionService>(sp => sp.GetRequiredService<MaxioSubscriptionService>());

        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildClientOptions(IConfiguration configuration, MaxioOptions options)
    {
        var environment = ServerEnvironment.Us;
        var environmentValue = configuration["MAXIO_ENVIRONMENT"];
        if (!string.IsNullOrWhiteSpace(environmentValue) &&
            environmentValue.Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            environment = ServerEnvironment.Eu;
        }

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio's Basic scheme expects the API key as the username and the literal
                // value "x" as the password. The API key is never logged or persisted here.
                Username = options.ApiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = AttemptTimeout
            }
        };

        if (environment == ServerEnvironment.Eu)
        {
            ApplySiteOverride(clientOptions.Server.Production.Eu, options);
        }
        else
        {
            ApplySiteOverride(clientOptions.Server.Production.Us, options);
        }

        return clientOptions;
    }

    private static void ApplySiteOverride(ProductionOptions.UsOptions productionNode, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            productionNode.Site = options.Subdomain;
        }
        else
        {
            productionNode.BaseUrl = options.BaseUrl;
        }
    }

    private static void ApplySiteOverride(ProductionOptions.EuOptions productionNode, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            productionNode.Site = options.Subdomain;
        }
        else
        {
            productionNode.BaseUrl = options.BaseUrl;
        }
    }
}
