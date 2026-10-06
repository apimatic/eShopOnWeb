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
    /// <summary>
    /// Name of the <see cref="IHttpClientFactory"/> client the Maxio SDK sends through. Tests replace its
    /// primary handler to run without network access.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>Bounds one HTTP attempt. The whole API request is bounded separately (see <see cref="MaxioSubscriptionBillingService"/>).</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Registers Maxio-backed subscription billing. Settings are read from the <c>Maxio</c> configuration section
    /// and validated when the host starts.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .PostConfigure(ApplyEnvironmentFallback)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop slightly above the SDK's own per-attempt timeout.
                client.Timeout = PerAttemptTimeout + TimeSpan.FromSeconds(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a long-lived singleton; recycle pooled connections so DNS changes are seen.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, CreateClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new MaxioBillingTimeouts());
        services.AddMemoryCache();
        services.AddScoped<SubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Builds the SDK options once; a rotated API key therefore takes effect on the next process start.
    /// </summary>
    public static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            // Maxio Basic auth: the API key is the username and the password is the literal "x".
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
            // GETs are retried by the SDK; POSTs (customer / subscription creation) never are.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = PerAttemptTimeout },
            // LoggerFactory is assigned explicitly so the SDK's log environment variable cannot switch body
            // logging on; bodies stay unlogged because customer creation carries names and e-mail addresses.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }

    // Deployments that only expose the MAXIO_* variables (no Maxio__* / user-secrets) still work; a value present
    // in the Maxio configuration section always wins.
    private static void ApplyEnvironmentFallback(MaxioSettings settings)
    {
        settings.ApiKey = FirstNonBlank(settings.ApiKey, Environment.GetEnvironmentVariable("MAXIO_API_KEY"));
        settings.Subdomain = FirstNonBlank(settings.Subdomain, Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"));
        settings.ProductFamilyHandle = FirstNonBlank(settings.ProductFamilyHandle, Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY"));
    }

    private static string? FirstNonBlank(string? configured, string? fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
