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

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Named HttpClient that carries Maxio traffic only, so its timeout and handler are not shared app-wide.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    // Per-attempt bounds. The whole-request budget lives in MaxioSubscriptionBillingService.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Registers Maxio Advanced Billing as the subscription billing provider. Settings are bound from the
    /// <c>Maxio</c> section and validated when the host starts, so a missing credential stops the app from booting.
    /// The client options are built once; a rotated API key takes effect on the next process restart.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding one HttpClient; recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey!,
                    // Maxio's documented Basic-auth password for API-key authentication.
                    Password = "x"
                },
                // Default retry verbs (GET/HEAD/PUT/OPTIONS) are kept on purpose: the POST writes are never resent.
                Retry = RetryOptions.Default() with { Timeout = AttemptTimeout },
                Logging = new LoggingOptions
                {
                    // Assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG variable cannot switch on body logging.
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    // Customer create bodies carry personal data (email, names).
                    LogRequestBody = false
                },
                TimeProvider = sp.GetRequiredService<TimeProvider>()
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain!;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
