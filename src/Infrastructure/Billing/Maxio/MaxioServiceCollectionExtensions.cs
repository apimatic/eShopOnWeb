using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated <see cref="HttpClient"/> the Maxio SDK client is built on.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Per-attempt bound on one HTTP exchange with Maxio (the SDK's own timeout).</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Registers subscription billing backed by Maxio Advanced Billing: validated <c>Maxio:</c> settings
    /// (the host refuses to start without them), one long-lived SDK client over a named HttpClient, the
    /// EF-backed claim store and <see cref="ISubscriptionBillingService"/>.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop for a hang before response headers; ends the call on any verb (not retried).
                client.Timeout = AttemptTimeout + TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient: recycle connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // Password is the literal "x" per the Maxio auth contract; the key is validated non-blank at startup.
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                // Default retry policy (GET/HEAD/PUT/OPTIONS only — never POST), with an interactive per-attempt bound.
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = AttemptTimeout },
                // Explicit factory: disables the SDK's log environment variable; bodies (customer PII) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                TimeProvider = sp.GetRequiredService<TimeProvider>()
            };

            if (settings.HasBaseUrlOverride)
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!; // verbatim override
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioSiteArchitecture>();
        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
