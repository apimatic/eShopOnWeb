using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Registers the payment gateway: binds <see cref="PaymentOptions"/> from the "PayPal"
/// configuration section (user secrets / environment variables) and wires a dedicated,
/// factory-managed HttpClient.
/// </summary>
public static class PayPalDependencies
{
    private const string HttpClientName = "PayPalGateway";

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop for a hung provider: bounds one attempt (the SDK's retry timeout
                // bounds the others). A whole-call budget lives in PayPalGateway.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IPaymentGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<PaymentOptions>>();
            var logger = sp.GetRequiredService<ILogger<PayPalGateway>>();
            return new PayPalGateway(httpClient, options, logger);
        });

        services.AddScoped<IPaymentService, ApplicationCore.Services.PaymentService>();
    }
}
