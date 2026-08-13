using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class TwilioNotificationServiceExtensions
{
    private const string TwilioMessagingHttpClient = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio messaging client and the SMS gateway. Expects <see cref="TwilioSettings"/>
    /// to have already been bound and validated (see the host's configuration wiring).
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services)
    {
        // Named HttpClient kept off the shared default client: a per-attempt timeout, plus a pooled
        // connection lifetime so DNS stays fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(TwilioMessagingHttpClient, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioMessagingHttpClient);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            // Twilio:BaseUrl (when present) overrides the messaging (api) server node only — used
            // verbatim for send/fetch/update/list. It does not govern the Lookup host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        return services;
    }
}
