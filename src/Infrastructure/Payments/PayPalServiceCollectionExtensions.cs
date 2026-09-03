using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Core.Authentication.OAuth2.ClientCredentials;
using PayPal.Core.Configuration;
using PayPal.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PayPalServiceCollectionExtensions
{
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetRequiredSection(PayPalSettings.SectionName))
            .Validate(settings => string.Equals(settings.Environment, "Sandbox",
                    StringComparison.OrdinalIgnoreCase),
                "PayPal:Environment must be Sandbox for this deployment.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                                  Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _),
                "PayPal:BaseUrl must be an absolute URL when provided.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ClientId) &&
                                  !string.IsNullOrWhiteSpace(settings.ClientSecret) &&
                                  settings.Currency.Length == 3 &&
                                  settings.Currency == settings.Currency.ToUpperInvariant(),
                "PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment, and PayPal:Currency must be configured.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var options = new PayPalClientOptions
            {
                Environment = ServerEnvironment.Production,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(20),
                    MaxRetries = 2
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);
            return new PayPalClient(httpClient, options);
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }
}
