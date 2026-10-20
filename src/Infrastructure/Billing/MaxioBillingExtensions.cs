using System;
using System.Collections.Generic;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingExtensions
{
    internal const string HttpClientName = "MaxioAdvancedBilling";

    // Per-attempt bound (SDK Retry.Timeout and HttpClient.Timeout backstop). The whole-call budget lives in MaxioSubscriptionService.
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maps the deployment's MAXIO_* environment variables onto the Maxio:* keys as the LOWEST-precedence source:
    /// user-secrets, appsettings and explicit Maxio__* environment variables all win over them.
    /// </summary>
    public static void AddMaxioEnvironmentVariableMapping(this IConfigurationBuilder configuration)
    {
        var mapped = new Dictionary<string, string?>();
        void Map(string envName, string key)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) mapped[key] = value;
        }
        Map("MAXIO_API_KEY", "Maxio:ApiKey");
        Map("MAXIO_SITE_SUBDOMAIN", "Maxio:Subdomain");
        Map("MAXIO_DEFAULT_PRODUCT_FAMILY", "Maxio:ProductFamilyHandle");

        configuration.Sources.Insert(0, new MemoryConfigurationSource { InitialData = mapped });
    }

    /// <summary>Registers options (validated at host start), the singleton Maxio client and the subscription service.</summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, c => c.Timeout = AttemptTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built ONCE here and captured by the singleton: a rotated key takes effect on restart only.
        services.AddSingleton(sp =>
        {
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                TimeProvider = sp.GetRequiredService<TimeProvider>(),
                BasicAuth = new BasicAuthCredentials { Username = maxio.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with
                {
                    Timeout = AttemptTimeout,
                    MaxRetries = 2   // applies to GETs only: the default HttpMethodsToRetry never resends a POST
                },
                // Explicit factory: MAXIOADVANCEDBILLINGCLIENT_LOG=trace can not switch request-body logging on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
                options.Server.Production.Us.BaseUrl = maxio.BaseUrl!;   // verbatim override
            else
                options.Server.Production.Us.Site = maxio.Subdomain!;

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
