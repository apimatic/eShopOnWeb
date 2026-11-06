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
    /// <summary>Per-attempt timeout; the whole-call budget is enforced by <see cref="MaxioSubscriptionService"/>.</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>());

        // Built once: a rotated API key takes effect on restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                // The password half of Basic auth is the constant "x" for this API.
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                // Reads may be resent by the SDK; POSTs are never resent (default HttpMethodsToRetry).
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = AttemptTimeout },
                // Explicit factory so MAXIO_ADVANCED_BILLING_LOG=trace can never force body logging.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                },
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            var http = new HttpClient(handler, disposeHandler: true) { Timeout = AttemptTimeout };
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
