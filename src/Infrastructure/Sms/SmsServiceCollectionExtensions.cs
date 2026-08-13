using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class SmsServiceCollectionExtensions
{
    private const string MessagingHttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio-backed SMS notification stack: a long-lived Twilio client over an
    /// IHttpClientFactory-managed HttpClient, the gateway, and the application services. The host is
    /// responsible for binding and validating <see cref="TwilioSettings"/> (see the web host's Program).
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // A named HttpClient keeps this SDK's timeout/handler off the shared default client. Timeout bounds a
        // single attempt (a hang); PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(MessagingHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(MessagingHttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            // Override the MESSAGING base URL only, on the environment the client is constructed with. The
            // lookups host (Default4) is intentionally left at its own default.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsNotificationGateway, TwilioSmsGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
