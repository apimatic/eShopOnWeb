using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public static class MaxioServiceCollectionExtensions
{
    public const string HTTP_CLIENT_NAME = "MaxioAdvancedBilling";

    // Maxio's Basic-auth scheme: the API key is the user name and the password is the literal "x".
    private const string BASIC_AUTH_PASSWORD = "x";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();

        services.AddHttpClient(HTTP_CLIENT_NAME, (sp, client) =>
            {
                // Backstop per attempt; the SDK's own Retry.Timeout fires first. The whole call is
                // bounded by the request budget token in the billing service.
                var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
                client.Timeout = settings.AttemptTimeout + TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton holding this HttpClient: recycle connections so DNS stays fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp => CreateClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME),
            sp.GetRequiredService<IOptions<MaxioSettings>>().Value,
            sp.GetRequiredService<ILoggerFactory>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IBillingClaimStore, BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    /// <summary>
    /// Builds the SDK client from validated settings. Credentials are captured once: a rotated key
    /// takes effect on the next process restart.
    /// </summary>
    public static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = BASIC_AUTH_PASSWORD
            },
            // Default verb list: GETs may be retried, POSTs (customer/subscription creation) are never resent.
            Retry = RetryOptions.Default() with { Timeout = settings.AttemptTimeout },
            Logging = new LoggingOptions
            {
                // Assigned explicitly so the SDK's log environment variable can never switch on body logging:
                // customer-create bodies carry e-mail addresses and names.
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
