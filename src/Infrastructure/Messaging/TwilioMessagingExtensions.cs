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
/// Wires the Twilio messaging integration into the service container: the strongly-typed settings, one
/// long-lived <see cref="TwilioSdkClient"/> over a dedicated pooled <see cref="HttpClient"/>, the
/// <see cref="ISmsSender"/> seam, and the domain services that drive the notification flows.
/// </summary>
public static class TwilioMessagingExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.SectionName);
        services.Configure<TwilioSettings>(section);

        // Refuse to boot on missing credentials — a deployment fault, surfaced now with the offending key
        // named and no value echoed, rather than as a 401 on the first message.
        var settings = section.Get<TwilioSettings>() ?? new TwilioSettings();
        var missing = settings.MissingRequiredKeys();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Twilio messaging is not configured. Missing required settings: {string.Join(", ", missing)}. " +
                "Set them via environment variables, user-secrets, or your secret store before starting the app.");
        }

        // Scheduling window for the domain layer, sourced from Twilio configuration.
        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            return new NotificationSchedulingSettings { FollowUpDelay = TimeSpan.FromDays(s.FollowUpDelayDays) };
        });

        // A dedicated named HttpClient keeps this SDK's timeout and handler off the shared default client,
        // and a pooled-connection lifetime keeps DNS fresh behind the long-lived (singleton) SDK client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = s.AccountSid,
                    Password = s.AuthToken
                },
                // A non-idempotent send that fails on the transport is retried on any verb; keep the retry
                // floor to minimise the chance of a duplicate message. Bound each attempt too.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(s.BaseUrl))
            {
                // Twilio:BaseUrl governs the messaging (api) host only; the Lookup host is left at default.
                options.Server.Default.Production.BaseUrl = s.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsSender, TwilioSmsSender>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}
