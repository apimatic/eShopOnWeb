using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceRegistration
{
    /// <summary>
    /// Binds the PayPal: configuration section and registers the PayPal client.
    /// The client is a singleton so the OAuth access token is cached across requests.
    /// </summary>
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        services.AddHttpClient("PayPal");

        services.AddSingleton<IPayPalClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PayPalClient>>();
            return new PayPalClient(httpClientFactory.CreateClient("PayPal"), settings, logger);
        });

        return services;
    }
}
