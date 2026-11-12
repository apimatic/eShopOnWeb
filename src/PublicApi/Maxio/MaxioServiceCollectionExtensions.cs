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
    private const string HTTP_CLIENT_NAME = "Maxio";

    /// <summary>
    /// Registers the Maxio client and the billing services. The settings are read and validated once,
    /// here, so the host refuses to start with missing credentials; a rotated secret needs a restart.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SECTION_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();

        services.AddSingleton(settings);

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var environment = settings.ResolveEnvironment();
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
                Environment = environment,
                // Per attempt; only GET/HEAD/PUT/OPTIONS are ever resent by the SDK.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10), MaxRetries = 2 },
                // Assigned explicitly so MAXIO_ADVANCED_BILLING_LOG=trace cannot switch body logging on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };

            if (environment == MaxioAdvancedBilling.Servers.ServerEnvironment.Eu)
            {
                var eu = options.Server.Production.Eu;
                eu.Site = settings.Subdomain?.Trim() ?? eu.Site;
                eu.BaseUrl = BaseUrlOrDefault(settings, eu.BaseUrl);
            }
            else
            {
                var us = options.Server.Production.Us;
                us.Site = settings.Subdomain?.Trim() ?? us.Site;
                us.BaseUrl = BaseUrlOrDefault(settings, us.BaseUrl);
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<MaxioErrors>();
        services.AddSingleton<PlanService>();
        services.AddSingleton<SubscriptionService>();
        return services;
    }

    // Maxio:BaseUrl, when set, is used verbatim instead of the address derived from the subdomain.
    private static string BaseUrlOrDefault(MaxioSettings settings, string derived) =>
        string.IsNullOrWhiteSpace(settings.BaseUrl) ? derived : settings.BaseUrl.Trim();
}
