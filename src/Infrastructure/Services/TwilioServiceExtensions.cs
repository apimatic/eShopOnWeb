using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Wires the Twilio-backed SMS notification feature into an application's service container: the SDK client
/// (over a dedicated, resilient <see cref="System.Net.Http.HttpClient"/>), the gateway, and the coordinating
/// application service. Credentials are read from configuration only and validated at startup — a missing
/// required setting refuses to boot rather than failing on the first request.
/// </summary>
public static class TwilioServiceExtensions
{
    private const string HttpClientName = "TwilioMessaging";
    private const string NotificationsSection = "Notifications";

    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new TwilioSettings();
        configuration.GetSection(TwilioSettings.SectionName).Bind(settings);
        ValidateOrThrow(settings);
        services.AddSingleton(settings);

        var notificationSettings = new NotificationSettings();
        configuration.GetSection(NotificationsSection).Bind(notificationSettings);
        if (notificationSettings.FollowUpDelayDays < 1)
            notificationSettings.FollowUpDelayDays = 3;
        services.AddSingleton(notificationSettings);

        // A dedicated named HttpClient keeps this SDK's timeout/handler off the app's shared default client.
        // Timeout bounds a single attempt (a hung provider must never pin a request thread); the pooled
        // connection lifetime keeps DNS fresh behind the long-lived (singleton) SDK client below.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // The optional override re-points the messaging ("Default"/api) host only — Lookups run on a
            // separate host and are intentionally left untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<ISmsNotificationService, SmsNotificationService>();
        return services;
    }

    private static void ValidateOrThrow(TwilioSettings settings)
    {
        Require(settings.AccountSid, $"{TwilioSettings.SectionName}:AccountSid");
        Require(settings.AuthToken, $"{TwilioSettings.SectionName}:AuthToken");
        Require(settings.FromNumber, $"{TwilioSettings.SectionName}:FromNumber");
        Require(settings.MessagingServiceSid, $"{TwilioSettings.SectionName}:MessagingServiceSid");
    }

    private static void Require(string value, string key)
    {
        // Name the missing key; never echo the value (present or absent).
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via environment variable or .NET user-secrets before starting the app.");
    }
}
