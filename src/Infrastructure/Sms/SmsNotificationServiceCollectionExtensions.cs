using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Registers the SMS order-notification feature: the Twilio client, the provider adapter, and the
/// application services that drive orders and their notifications.
/// </summary>
public static class SmsNotificationServiceCollectionExtensions
{
    private const string TwilioHttpClientName = "TwilioMessaging";

    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.ConfigSection));

        // A named HttpClient keeps this pipeline (timeout, connection lifetime) off the shared default client.
        services.AddHttpClient(TwilioHttpClientName, client =>
            {
                // Bounds a single attempt. A total-call budget is applied per request in the endpoints.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The Twilio client below is a singleton, so keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The Twilio client is lightweight and meant to be long-lived: construct once, reuse.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioHttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) }
            };

            // Optional override for the messaging API host only (send / read / list). Applied before
            // construction so it takes effect on the environment actually selected.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsService, TwilioSmsService>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderMessagingService, OrderMessagingService>();

        return services;
    }
}
