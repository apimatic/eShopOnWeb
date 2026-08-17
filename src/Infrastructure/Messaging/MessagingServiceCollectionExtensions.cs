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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Registers the Twilio-backed SMS order-notification stack: settings (validated at startup), the
/// long-lived Twilio client, the provider seam, and the order/contact-number services.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind Twilio:* and refuse to boot if a credential is missing (a deployment fault, not a request fault).
        // Messages name the missing key and never echo a value.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.ConfigurationSection))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // One long-lived client for the app. A SocketsHttpHandler with a pooled-connection lifetime keeps DNS
        // fresh behind the singleton; HttpClient.Timeout is a per-attempt backstop (the whole-call bound is a
        // linked CancellationToken applied in the provider).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Sends are non-idempotent; keep the transport-retry exposure to the minimum the pipeline allows.
                Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(15) }
            };

            // Twilio:BaseUrl (when set) overrides only the messaging (Default) node, verbatim. Lookup stays on its own host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<IMessagingProvider, TwilioMessagingProvider>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
