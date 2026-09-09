using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing SDK client and the
    /// <see cref="IMaxioBillingService"/> application service. Binds settings from the
    /// "Maxio" configuration section (user secrets / environment variables supply values).
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName));

        // Named client: keeps timeout, handler pipeline and DNS freshness scoped to the
        // Maxio SDK instead of the shared default IHttpClientFactory client.
        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop for a hung attempt. The retry pipeline gets a fresh timeout per
                // attempt, so this bounds one attempt, not the whole call; the service also
                // bounds each call with a cancellation-token budget.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived; a singleton keeps the factory-managed handler
        // pipeline (with PooledConnectionLifetime above refreshing DNS) behind it.
        services.AddSingleton<IMaxioBillingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            options.Validate();

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var client = new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options));

            return new MaxioBillingService(
                client,
                options,
                sp.GetRequiredService<ILogger<MaxioBillingService>>());
        });

        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            // MaxRetries stays at the floor of 1: transport failures are retried on every
            // verb regardless of configuration, so a subscription/customer POST can reach
            // the provider twice. Both writes carry a client reference and the service
            // reconciles by reference afterwards, which makes the resend harmless.
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10)
            },
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio Advanced Billing authenticates with HTTP Basic where the API key is
                // the username and the password is the literal "x".
                Username = options.ApiKey,
                Password = "x"
            }
        };

        clientOptions.Server.Production.Us.Site = options.Subdomain;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }

        return clientOptions;
    }
}
