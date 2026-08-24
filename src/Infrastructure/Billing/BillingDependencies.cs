using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class BillingDependencies
{
    internal const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, client =>
            {
                // Per-attempt backstop against a hung provider (default 100s is an outage, not a timeout).
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (!settings.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl) " +
                    "and Maxio:ProductFamilyHandle via user-secrets or environment variables.");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var isEu = string.Equals(settings.Environment, "Eu", StringComparison.OrdinalIgnoreCase);

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (isEu) { options.Server.Production.Eu.BaseUrl = settings.BaseUrl; }
                else { options.Server.Production.Us.BaseUrl = settings.BaseUrl; }
            }
            else
            {
                if (isEu) { options.Server.Production.Eu.Site = settings.Subdomain; }
                else { options.Server.Production.Us.Site = settings.Subdomain; }
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
