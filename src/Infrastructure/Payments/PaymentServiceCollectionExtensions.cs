using System;
using System.Net.Http;
using AdyenApIs;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>
    /// Registers order payments through Adyen. Settings are validated when the host starts: a missing or blank
    /// Adyen:* value stops the app from booting instead of surfacing as a 401 on the first payment.
    /// </summary>
    /// <param name="returnUrl">Absolute URL Adyen would send a shopper back to after a redirect.</param>
    public static IServiceCollection AddAdyenPayments(this IServiceCollection services, IConfiguration configuration, string returnUrl)
    {
        services.AddOptions<AdyenSettings>()
            .Bind(configuration.GetSection(AdyenSettings.SectionName))
            .PostConfigure(settings =>
            {
                if (string.IsNullOrWhiteSpace(settings.ReturnUrl)) settings.ReturnUrl = returnUrl;
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdyenSettings>, AdyenSettingsValidator>();

        services.AddHttpClient(AdyenClientFactory.HttpClientName, (sp, httpClient) =>
            {
                // Backstop per attempt, just above the SDK's own per-attempt timeout.
                httpClient.Timeout = sp.GetRequiredService<IOptions<AdyenSettings>>().Value.AttemptTimeout + TimeSpan.FromSeconds(5);
            })
            // The SDK client below is a singleton holding this HttpClient: recycle connections so DNS changes are picked up.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        // Built once from the validated settings; a rotated API key takes effect on the next process start.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AdyenSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(AdyenClientFactory.HttpClientName);
            return new AdyenApIsClient(httpClient, AdyenClientFactory.CreateOptions(settings, sp.GetRequiredService<ILoggerFactory>()));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new PaymentOptions());
        services.AddScoped<IPaymentGateway, AdyenPaymentGateway>();
        services.AddScoped<IPaymentStore, EfPaymentStore>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        return services;
    }
}
