using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient used by the Maxio SDK (kept off the shared default client).</summary>
    public const string HttpClientName = "Maxio";

    // Per-attempt bounds. The whole-request budget is enforced by SubscriptionBillingService.
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail fast: the host refuses to start when a required Maxio setting is missing or blank.
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(MaxioOptions.HasValidBaseUrl, $"{MaxioOptions.SectionName}:BaseUrl must be an absolute http(s) URL when set.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are captured once here: a rotated API key takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<MaxioBillingGateway>();
        services.AddScoped<SubscriptionBillingService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio Basic auth: the API key is the username, the password is the literal "x".
                Username = settings.ApiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                // Only reads are ever resent by the SDK; POSTs (customer/subscription creation) go out once.
                HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head],
                MaxRetries = 2,
                Timeout = PerAttemptTimeout
            },
            Logging = new LoggingOptions
            {
                // Explicit factory disarms the MAXIOADVANCEDBILLINGCLIENT_LOG env var; customer bodies carry
                // personal data (name, email), so request bodies are never logged.
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        options.Server.Production.Us.Site = settings.Subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
