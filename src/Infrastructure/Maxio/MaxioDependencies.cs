using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    /// <summary>
    /// Name of the named HttpClient the Maxio client uses, so its pipeline
    /// (timeout, pooled handler lifetime, test stubs) stays off the shared
    /// default client.
    /// </summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddHttpClient(HttpClientName, httpClient =>
            {
                // Backstop for a hung provider: bounds one attempt; the whole
                // call is additionally bounded in MaxioSubscriptionService.
                httpClient.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so rotate pooled
                // connections to keep DNS fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            Validate(options);

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    // Per-attempt bound; keep a hung attempt from pinning the
                    // pipeline for the 100s default.
                    Timeout = TimeSpan.FromSeconds(15)
                },
                // Maxio auth is HTTP Basic where the username is the API key
                // and the password is the literal "x".
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                }
            };
            clientOptions.Server.Production.Us.Site = options.Subdomain;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                // Verbatim override of the derived base address.
                clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddSingleton<ISubscriptionService, MaxioSubscriptionService>();
    }

    private static void Validate(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: {MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)} is missing.");
        }
        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: {MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)} is missing.");
        }
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: {MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)} is missing.");
        }
    }
}
