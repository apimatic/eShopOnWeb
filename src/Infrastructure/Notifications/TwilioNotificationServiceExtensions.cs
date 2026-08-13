using System;
using System.Net.Http;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Registers the Twilio-backed SMS notification stack: the SDK client (long-lived, over a
/// lifetime-managed connection pool), the provider seam, and the application services that use it.
/// Configuration binding and startup validation of <see cref="TwilioSettings"/> is done by the host
/// (see the PublicApi composition root) so a missing secret refuses to boot.
/// </summary>
public static class TwilioNotificationServiceExtensions
{
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services)
    {
        // The Twilio client is long-lived: build it once as a singleton over a single HttpClient whose
        // SocketsHttpHandler recycles pooled connections so DNS changes are picked up. The per-attempt
        // timeout bounds a hung provider; retries are held to the floor so a non-idempotent CreateMessage
        // is not silently re-sent by the transport retry path.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;

            // Defensive backstop in addition to the host's ValidateOnStart — never echoes the values.
            Guard.Against.NullOrWhiteSpace(settings.AccountSid, "Twilio:AccountSid");
            Guard.Against.NullOrWhiteSpace(settings.AuthToken, "Twilio:AuthToken");
            Guard.Against.NullOrWhiteSpace(settings.FromNumber, "Twilio:FromNumber");
            Guard.Against.NullOrWhiteSpace(settings.MessagingServiceSid, "Twilio:MessagingServiceSid");

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Twilio:BaseUrl overrides ONLY the messaging host (Server.Default). Lookup keeps its own
            // default host, which is exactly the isolation the requirement calls for.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsNotificationProvider, TwilioSmsNotificationProvider>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<IPublicApiOrderService, PublicApiOrderService>();

        return services;
    }
}
