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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient, so Maxio's timeout and handlers are not shared with other consumers.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    // The SDK documents the Basic-auth password half as the literal "x"; the API key is the user name.
    private const string ApiKeyPassword = "x";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. Settings come from the
    /// <c>Maxio:</c> section and are validated when the host starts. Requires an
    /// <see cref="ISubscriptionClaimStore"/> registration.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Backstop only: the SDK's own per-attempt timeout (shorter) fires first and is retryable for reads.
                httpClient.Timeout = settings.AttemptTimeout + TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once, when the client is first resolved: a rotated API key takes effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        if (!ServerEnvironment.TryGetKnownValue(settings.Environment?.Trim().ToUpperInvariant(), out var environment))
        {
            throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.");
        }

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = ApiKeyPassword },
            TimeProvider = timeProvider,
            // Default retry methods (GET/HEAD/PUT/OPTIONS) are kept: the POSTs this integration makes are never resent.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = settings.MaxReadRetries,
                Timeout = settings.AttemptTimeout,
            },
            // LoggerFactory is set explicitly (this also disables the MAXIOADVANCEDBILLINGCLIENT_LOG switch).
            // Bodies and headers stay off: customer creation carries personal data.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        var site = settings.Subdomain?.Trim() ?? string.Empty;
        if (environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = site;
            if (baseUrl is not null)
            {
                options.Server.Production.Eu.BaseUrl = baseUrl;
            }
        }
        else
        {
            options.Server.Production.Us.Site = site;
            if (baseUrl is not null)
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
            }
        }

        return options;
    }
}
