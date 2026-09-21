using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers the Maxio Advanced Billing client and <see cref="ISubscriptionBillingService"/>.
    ///
    /// The <c>Maxio:</c> settings are validated at startup (<c>ValidateOnStart</c>), so a missing or blank
    /// <c>ApiKey</c>/<c>Subdomain</c>/<c>ProductFamilyHandle</c> fails the host at boot rather than surfacing
    /// as a 401 on the first call. The SDK client is built over a dedicated, named <see cref="HttpClient"/>
    /// (isolated timeout + pooled-connection recycling) with an explicit <c>LoggerFactory</c> so the SDK's
    /// body-logging environment variable cannot arm request-body logging from outside the code.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                // Per-attempt backstop for a hung socket; the whole-operation budget is enforced by the service's token.
                http.Timeout = TimeSpan.FromSeconds(opts.PerAttemptTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keeps DNS fresh behind the long-lived (singleton) SDK client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived: it eagerly builds its resilience pipeline and auth. Options are
        // captured once here, so a rotated key takes effect only after a process restart (documented).
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = opts.ApiKey, // Maxio (Chargify) API key
                    Password = "x",         // fixed per Maxio Basic-auth convention
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(opts.PerAttemptTimeoutSeconds),
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,     // request bodies carry customer PII; never log them
                    LogRequestHeaders = false,  // Authorization header would otherwise be present
                    LogResponseHeaders = false,
                },
            };

            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                clientOptions.Server.Production.Us.BaseUrl = opts.BaseUrl!; // used verbatim
            else
                clientOptions.Server.Production.Us.Site = opts.Subdomain;   // https://{subdomain}.chargify.com

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
