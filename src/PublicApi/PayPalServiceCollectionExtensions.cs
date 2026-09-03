using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalServiceCollectionExtensions
{
    public static IServiceCollection AddPayPalPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetRequiredSection(PayPalSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(x => string.Equals(x.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase),
                "Only the PayPal sandbox environment is permitted by this application build.")
            .Validate(x => string.IsNullOrWhiteSpace(x.BaseUrl) ||
                Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "PayPal:BaseUrl must be an absolute HTTPS URI.")
            .ValidateOnStart();

        services.AddHttpClient("PayPal", client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(20) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;

            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("PayPal");
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<PaymentOperationLock>();
        services.AddScoped<PayPalGateway>();
        services.AddScoped<IPaymentWorkflowService, PaymentWorkflowService>();
        return services;
    }
}
