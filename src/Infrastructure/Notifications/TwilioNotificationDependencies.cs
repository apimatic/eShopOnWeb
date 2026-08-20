using System;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class TwilioNotificationDependencies
{
    /// <summary>
    /// Registers Twilio-backed SMS notifications: the validated <see cref="TwilioSettings"/>, a
    /// long-lived <see cref="TwilioSdkClient"/> over a dedicated named HttpClient, and the gateway and
    /// application services that drive contact numbers and order notifications.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate credentials at startup so a PARTIAL misconfiguration refuses to boot rather than
        // surfacing as a provider 401 on the first unlucky request. Configuration is all-or-none: a fully
        // configured Twilio section enables SMS; a completely absent one leaves SMS disabled (the posture
        // dev/test hosts run in). Anything in between is a deployment fault and fails fast.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s =>
            {
                var values = new[] { s.AccountSid, s.AuthToken, s.FromNumber, s.MessagingServiceSid };
                var anySet = values.Any(v => !string.IsNullOrWhiteSpace(v));
                var allSet = values.All(v => !string.IsNullOrWhiteSpace(v));
                return !anySet || allSet;
            }, "Twilio configuration is incomplete: set all of Twilio:AccountSid, Twilio:AuthToken, " +
               "Twilio:FromNumber and Twilio:MessagingServiceSid, or none of them to leave SMS disabled.")
            .ValidateOnStart();

        // A dedicated HttpClient keeps this SDK's timeout and handler off the shared default client.
        const string clientName = "TwilioMessaging";
        services.AddHttpClient(clientName, c =>
            {
                // Bounds a single attempt; the SDK retry timeout is set on the options below.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The client below is a singleton, so keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Non-idempotent writes (send/schedule/resend): keep the transport-retry exposure minimal
                // and bound each attempt. App-side idempotency guards the resend path.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Optional override for the MESSAGING API host only. Used verbatim when present; the separate
            // phone-lookup host is never redirected by it.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            // Twilio__LookupsBaseUrl overrides ONLY the Lookup host, verbatim.
            var lookupsBaseUrl = System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl");
            if (!string.IsNullOrEmpty(lookupsBaseUrl))
                options.Server.Default4.Production.BaseUrl = lookupsBaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
