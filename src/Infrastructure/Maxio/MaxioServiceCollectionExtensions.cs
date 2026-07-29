using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>
    /// Registers Maxio Advanced Billing: binds <see cref="MaxioSettings"/> from the <c>Maxio</c>
    /// configuration section, constructs a single long-lived SDK client, and wires the
    /// <see cref="ISubscriptionBillingService"/> implementation.
    /// </summary>
    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioSettings>(configuration.GetSection(MaxioSettings.CONFIG_NAME));

        // The SDK client is lightweight controller wrappers over one HTTP pipeline and is meant to
        // be long-lived — construct it once as a singleton. The HttpClient it owns lives for the
        // app lifetime; PooledConnectionLifetime rotates the underlying connections so DNS/cert
        // changes are eventually picked up.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
                throw new InvalidOperationException("Maxio:ApiKey is not configured.");
            if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
                throw new InvalidOperationException("Either Maxio:Subdomain or Maxio:BaseUrl must be configured.");

            var httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    // Basic auth: the API key is the username, the password is the literal "x".
                    Username = settings.ApiKey,
                    Password = "x"
                }
            };

            // Subdomain feeds the host template; an explicit BaseUrl overrides it verbatim.
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
