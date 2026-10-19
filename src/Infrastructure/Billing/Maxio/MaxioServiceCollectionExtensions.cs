using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// Registers recurring-subscription billing backed by Maxio Advanced Billing. The host refuses to
    /// start when the <c>Maxio</c> settings are incomplete.
    /// </summary>
    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                // Backstop per attempt; the SDK's own per-attempt timeout fires first.
                client.Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton: recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Built once: a rotated API key takes effect on process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionEnrollmentStore, EfSubscriptionEnrollmentStore>();
        services.AddScoped<SubscriptionService>();
        return services;
    }

    public static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
            // Default verbs kept: GETs retry; POST writes are never resent by the SDK.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds) },
            // Explicit factory (also disables the SDK's env-var logging switch); bodies carry customer PII.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (settings.IsEu)
        {
            if (hasBaseUrl) options.Server.Production.Eu.BaseUrl = settings.BaseUrl!;
            else options.Server.Production.Eu.Site = settings.Subdomain!;
        }
        else
        {
            if (hasBaseUrl) options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else options.Server.Production.Us.Site = settings.Subdomain!;
        }
        return options;
    }
}
