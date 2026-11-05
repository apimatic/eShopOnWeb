using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    /// <summary>
    /// Binds the "Maxio" section, refuses to start when a required value is missing, and registers
    /// the Maxio SDK client (singleton; the options are captured once, so a rotated key needs a restart)
    /// and the subscription service.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>() ?? new MaxioSettings();

        RequireValue(settings.ApiKey, $"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ApiKey)}");
        RequireValue(settings.Subdomain, $"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.Subdomain)}");
        RequireValue(settings.ProductFamilyHandle, $"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.ProductFamilyHandle)}");
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) && !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{MaxioSettings.CONFIG_NAME}:{nameof(MaxioSettings.BaseUrl)} must be an absolute URL when set.");
        }

        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(5), MaxRetries = 2 },
                // Assigned explicitly so MAXIO_ADVANCED_BILLING_LOG can never switch on body logging.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<SubscriptionService>();
        return services;
    }

    private static void RequireValue(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} is not configured. Set it via user-secrets or the environment before starting the app.");
        }
    }
}
