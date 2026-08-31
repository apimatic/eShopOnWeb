using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class TwilioRegistration
{
    private const string HttpClientName = "Twilio";

    public static void AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid),
                "Twilio:AccountSid is not configured. Set it via user-secrets or environment variables before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken),
                "Twilio:AuthToken is not configured. Set it via user-secrets or environment variables before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber),
                "Twilio:FromNumber is not configured. Set it via user-secrets or environment variables before starting the app.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid),
                "Twilio:MessagingServiceSid is not configured. Set it via user-secrets or environment variables before starting the app.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Bounds one attempt; the gateway holds the whole-call budget.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            GuardMissing(settings);

            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Messaging API group only; Lookup validation stays on the provider's lookups host.
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<INotificationGateway, TwilioNotificationGateway>();
    }

    private static void GuardMissing(TwilioSettings settings)
    {
        // Names the missing key; never echoes a value.
        if (string.IsNullOrWhiteSpace(settings.AccountSid))
            throw new InvalidOperationException("Twilio:AccountSid is not configured. Set it via user-secrets or environment variables before starting the app.");
        if (string.IsNullOrWhiteSpace(settings.AuthToken))
            throw new InvalidOperationException("Twilio:AuthToken is not configured. Set it via user-secrets or environment variables before starting the app.");
        if (string.IsNullOrWhiteSpace(settings.FromNumber))
            throw new InvalidOperationException("Twilio:FromNumber is not configured. Set it via user-secrets or environment variables before starting the app.");
        if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            throw new InvalidOperationException("Twilio:MessagingServiceSid is not configured. Set it via user-secrets or environment variables before starting the app.");
    }
}
