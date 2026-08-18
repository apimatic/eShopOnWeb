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
/// Wires the Twilio SMS integration into the host: the strongly-typed, startup-validated settings, one
/// long-lived <see cref="TwilioSdkClient"/> over a named <see cref="System.Net.Http.IHttpClientFactory"/>
/// client, the <see cref="ISmsProvider"/> gateway, and the order-notification orchestration.
/// </summary>
public static class TwilioServiceCollectionExtensions
{
    private const string ClientName = "TwilioMessaging";

    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate the Twilio settings. ValidateOnStart makes a missing credential a startup failure,
        // not a first-request 401.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.ConfigSection))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Make the resolved settings directly injectable (the provider takes TwilioSettings).
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);

        // A named HttpClient keeps this SDK's timeout/handler off the shared default client. A per-attempt
        // timeout backstops a hang; PooledConnectionLifetime keeps DNS fresh behind the long-lived client.
        services.AddHttpClient(ClientName, (sp, c) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                c.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // MaxRetries has a floor of 1 (still two attempts): keep it there to minimise the chance a
                // transport retry re-sends a non-idempotent write (an SMS costs real money). The whole-call
                // deadline lives in the provider; this bounds a single attempt.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds)
                }
            };

            // Override ONLY the messaging (Default/api.twilio.com) node when Twilio:BaseUrl is set — used
            // verbatim. The Lookup API rides a different node and is intentionally left on its own host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ISmsProvider, TwilioSmsProvider>();
        services.AddSingleton(new OrderNotificationOptions());
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
