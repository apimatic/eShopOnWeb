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

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>
    /// Name of the <see cref="HttpClient"/> the Maxio SDK client uses. Kept off the shared default client so
    /// its timeout and handler never affect other consumers (tests replace its primary handler).
    /// </summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>The Basic-auth password Maxio documents for API-key authentication ("The password is x").</summary>
    private const string ApiKeyPassword = "x";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, http) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Backstop only: the SDK's per-attempt timeout (below) fires first and, for GETs, is retried.
                http.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient; recycle pooled connections so DNS changes apply.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddScoped<IBillingClaimStore, EfBillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Set in code from Maxio:ApiKey (validated non-blank at startup), so the SDK's own
            // MAXIO_ADVANCED_BILLING_BASIC_AUTH_* environment fallback is never consulted.
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = ApiKeyPassword
            },
            // Default retry policy (GET/HEAD/PUT/OPTIONS only — POSTs are never resent), with a per-attempt
            // timeout sized for an interactive request. The whole call is bounded by the service's budget token.
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds),
                MaxRetries = 2
            },
            // Explicit logger factory: request/response lines go to the app's logs, and the SDK's
            // MAXIO_ADVANCED_BILLING_LOG variable cannot switch on body logging (customer PII) behind our back.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        var subdomain = string.IsNullOrWhiteSpace(settings.Subdomain) ? null : settings.Subdomain.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();

        // Configure the node of the environment selected above — the only one the client reads.
        if (settings.IsEu)
        {
            if (subdomain is not null) options.Server.Production.Eu.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (subdomain is not null) options.Server.Production.Us.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }
}
