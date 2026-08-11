using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PayPal integration: binds the <c>PayPal:</c> configuration section and wires
    /// <see cref="IPayPalGateway"/> onto a long-lived <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PayPalSettings.SectionName);
        var settings = new PayPalSettings
        {
            ClientId = section["ClientId"],
            ClientSecret = section["ClientSecret"],
            Environment = section["Environment"],
            Currency = section["Currency"],
            BaseUrl = section["BaseUrl"]
        };

        services.AddSingleton(Options.Create(settings));

        // A single reused HttpClient for the app's lifetime (the recommended pattern for a client
        // that talks to one host). The gateway itself is stateless beyond a cached access token.
        services.AddSingleton<IPayPalGateway>(sp =>
        {
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            return new PayPalGateway(
                httpClient,
                sp.GetRequiredService<IOptions<PayPalSettings>>(),
                sp.GetRequiredService<ILogger<PayPalGateway>>());
        });

        return services;
    }
}
