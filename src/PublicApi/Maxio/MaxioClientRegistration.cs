using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Registers the Maxio Advanced Billing SDK client over a named <c>IHttpClientFactory</c>
/// client (singleton client, pooled connections) and the subscription service.
/// </summary>
public static class MaxioClientRegistration
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (!settings.IsConfigured)
        {
            services.AddSingleton<IMaxioSubscriptionService, NotConfiguredMaxioSubscriptionService>();
            return services;
        }

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));
        });
        services.AddSingleton<IMaxioSubscriptionService, MaxioSubscriptionService>();
        return services;
    }

    private static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = string.Equals(settings.Environment?.Trim(), "eu", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            // A failed create may still have landed at the provider; keep re-sends minimal and
            // reconcile afterwards (see MaxioSubscriptionService.ReconcileAfterAmbiguousCreateAsync).
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.FromSeconds(1),
                Timeout = TimeSpan.FromSeconds(12)
            }
        };

        options.Server.Production.Us.Site = settings.Subdomain;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}