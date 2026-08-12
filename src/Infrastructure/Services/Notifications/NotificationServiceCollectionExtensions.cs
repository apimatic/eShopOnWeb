using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services.Notifications;

/// <summary>
/// Wires up SMS order notifications: binds the <c>Twilio:</c> settings, registers the twilio-sdk client
/// (credentials + messaging-only base-URL override + retries disabled to avoid duplicate sends), the
/// provider seam, and the notification orchestration service.
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioSettings.SectionName);
        services.Configure<TwilioSettings>(section);

        var settings = section.Get<TwilioSettings>() ?? new TwilioSettings();

        services.AddTwilioSdkClient(options =>
        {
            options.AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            };

            // When Twilio:BaseUrl is set, use it verbatim as the messaging-API base address only. The
            // Default node backs every Api20100401Message call; Lookup (the Default4 node) is untouched.
            if (!string.IsNullOrEmpty(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            // Never auto-retry: a re-executed CreateMessage (POST) would send a second real SMS.
            options.Retry = RetryOptions.Disabled();
        });

        services.AddSingleton<ISmsNotificationProvider, TwilioSmsNotificationProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
