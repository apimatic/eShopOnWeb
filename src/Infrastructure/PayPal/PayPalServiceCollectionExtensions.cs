using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public static class PayPalServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything needed to talk to PayPal: settings bound from the <c>PayPal:</c> section,
    /// the cached OAuth token provider, and the typed REST client. The token provider is a singleton so
    /// the access token is cached across requests.
    /// </summary>
    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Expose the bound settings both as the concrete type (for the currency the domain needs) and
        // as IPaymentSettings for the application layer.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PayPalSettings>>().Value);
        services.AddSingleton<IPaymentSettings>(sp => sp.GetRequiredService<PayPalSettings>());

        // Named client used by the singleton token provider (a singleton must not hold a typed HttpClient).
        services.AddHttpClient(PayPalTokenProvider.HttpClientName, ConfigureHttpClient);
        services.AddSingleton<PayPalTokenProvider>();

        // The REST client is a typed client; its HttpClient lifetime is managed by the factory.
        services.AddHttpClient<IPayPalClient, PayPalHttpClient>(ConfigureHttpClient);

        return services;
    }

    private static void ConfigureHttpClient(IServiceProvider sp, System.Net.Http.HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(100);
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }
}
