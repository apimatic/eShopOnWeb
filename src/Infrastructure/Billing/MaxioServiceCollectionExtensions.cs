using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client is built on.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. Settings come from the
    /// <c>Maxio</c> configuration section and are validated when the host starts: a missing or blank
    /// <c>Maxio:ApiKey</c>, <c>Maxio:ProductFamilyHandle</c> or <c>Maxio:Subdomain</c> (unless
    /// <c>Maxio:BaseUrl</c> is set) stops the host from starting.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        // A dedicated named client keeps this pipeline (timeouts, handlers) off the app's shared default client.
        services.AddHttpClient(HttpClientName, c =>
            {
                // Backstop per attempt; the request budget (MaxioSettings.RequestBudgetSeconds) bounds the whole call.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient: recycle pooled connections so DNS
                // changes on the provider side are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Options are read once here; a rotated API key takes effect on the next process restart.
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,

            // Always set in code, so the SDK's own MAXIO_ADVANCED_BILLING_BASIC_AUTH_* fallback is never the source.
            // Maxio: the username is the API key and the password is the literal "x".
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!.Trim(),
                Password = "x"
            },

            // GET/HEAD/PUT/OPTIONS only (SDK default) — the two POSTs in scope are never resent by the SDK.
            // 10s per attempt, 2 retries; the request budget caps the total.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10)
            },

            // Explicit factory: disables the MAXIO_ADVANCED_BILLING_LOG env switch (which at 'trace' would log
            // customer PII in request bodies). Bodies are never logged.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Explicit override: used verbatim instead of deriving the host from the subdomain.
            var baseUrl = settings.BaseUrl.Trim();
            options.Server.Production.Us.BaseUrl = baseUrl;
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
            options.Server.Production.Eu.Site = settings.Subdomain.Trim();
        }

        return options;
    }
}
