using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Registers the Twilio-backed order-notification integration: binds and fail-fast validates settings,
/// constructs the Twilio SDK client with Basic auth and the messaging base-URL override, and wires the
/// gateway and application services.
/// </summary>
public static class TwilioNotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioOrderNotifications(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.SectionName);
        services.Configure<TwilioSettings>(section);

        var settings = section.Get<TwilioSettings>() ?? new TwilioSettings();

        // Fail-fast: refuse to start on a missing credential rather than discovering it as a 401 in production.
        // Basic auth needs both halves, and the send/reconcile paths need the From number and service SID —
        // a blank part is not a missing one, so each is checked. BaseUrl is genuinely optional.
        RequireConfigured(settings.AccountSid, "Twilio:AccountSid");
        RequireConfigured(settings.AuthToken, "Twilio:AuthToken");
        RequireConfigured(settings.FromNumber, "Twilio:FromNumber");
        RequireConfigured(settings.MessagingServiceSid, "Twilio:MessagingServiceSid");

        services.AddTwilioSdkClient(options =>
        {
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };
            options.Environment = ServerEnvironment.Production;

            // The lookups request URL carries the phone number in its (unredacted) path, so the SDK's own
            // request logger is silenced entirely — this app's number never reaches a log. Setting
            // LoggerFactory explicitly also neutralises the TWILIOCLIENT_LOG environment variable, and the
            // gateway does its own SID/status-only logging.
            options.Logging = new LoggingOptions
            {
                LoggerFactory = NullLoggerFactory.Instance,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            };

            // Twilio:BaseUrl overrides ONLY the messaging API (server group Default → api.twilio.com), through
            // which messages are sent, read and reconciled. Lookups (group Default4) is deliberately untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }
        });

        services.AddScoped<ISmsProviderGateway, TwilioSmsProviderGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationAdminService, NotificationAdminService>();

        return services;
    }

    private static void RequireConfigured(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Name the key; never echo the value (present or absent).
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets or environment configuration before starting the app.");
        }
    }
}
