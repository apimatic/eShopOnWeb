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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio messaging client, the SMS gateway, and the order-notification service.
    /// Credentials are bound from the <c>Twilio:</c> configuration section and validated at startup, so a
    /// missing secret is a boot-time deployment fault rather than a first-request 401.
    /// </summary>
    public static IServiceCollection AddTwilioSmsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate at startup and refuse to boot on a missing credential — a missing secret is a
        // deployment fault, not a first-request 401. Each check names its config key and never echoes a value.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        services.AddTransient<SingleSendGuardHandler>();

        // A named HttpClient keeps this pipeline (guard handler, timeout, pooled-connection lifetime) off the
        // shared default client. Timeout bounds one attempt; PooledConnectionLifetime keeps DNS fresh behind
        // the long-lived singleton client below.
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler<SingleSendGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // MaxRetries floors at 1 (two attempts). A conservative per-attempt timeout plus the
                // single-send guard keep a create-message POST from ever being sent twice.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Twilio:BaseUrl overrides ONLY the messaging host (the Default server node). The lookup host
            // (Default4) is intentionally left untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
