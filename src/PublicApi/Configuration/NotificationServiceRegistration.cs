using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires the Twilio-backed order-notification feature: strongly-typed settings with a startup fail-fast,
/// the SDK client (basic auth, a bounded per-attempt timeout, optional messaging base-URL override), the
/// provider gateway, and the orchestration service.
/// </summary>
public static class NotificationServiceRegistration
{
    public static IServiceCollection AddOrderSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail fast at startup if any credential is missing or blank — never surface it as a later 401.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();

        services.AddTwilioSdkClient(options =>
        {
            // Basic auth: account SID as username, auth token as password (the documented pattern).
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // Per-attempt timeout (the whole-call budget is enforced at the gateway via a CancellationToken).
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) };

            // Twilio:BaseUrl governs only the messaging API (server group Default = api.twilio.com).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }
        });

        services.AddSingleton(new NotificationOptions { FollowUpDelayDays = settings.FollowUpDelayDays });
        services.AddScoped<ISmsGateway, TwilioMessagingGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
