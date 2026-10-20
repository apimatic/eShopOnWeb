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

public static class MaxioBillingServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> dedicated to Maxio, kept off the shared default client.</summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing. Settings come from the <c>Maxio:</c>
    /// section and are validated when the host starts, so a missing credential stops the app from booting.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                // Backstop for one attempt; the SDK's own per-attempt timeout fires first.
                var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                httpClient.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient: recycle connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The options (and so the API key) are captured once: a rotated key takes effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<TimeProvider>()));
        });

        services.AddSingleton<MaxioSiteSettingsCache>();
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions settings, ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.UsesEuEnvironment ? ServerEnvironment.Eu : ServerEnvironment.Us,
            // Maxio Basic auth: the API key is the user name and the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            // POSTs stay out of HttpMethodsToRetry (the default), so a create is never resent by the SDK.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds) },
            // Assigning the factory explicitly keeps the MAXIOADVANCEDBILLINGCLIENT_LOG variable from switching on
            // body logging; customer creation carries personal data (name, email).
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            },
            TimeProvider = timeProvider
        };

        // Only the selected environment's server options are read, so configure that one.
        var subdomain = settings.Subdomain?.Trim();
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        if (settings.UsesEuEnvironment)
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Eu.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            if (!string.IsNullOrEmpty(subdomain)) options.Server.Production.Us.Site = subdomain;
            if (baseUrl is not null) options.Server.Production.Us.BaseUrl = baseUrl;
        }

        return options;
    }
}
