using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the Twilio: configuration section (values supplied via
    /// user-secrets/environment) and registers the hand-written,
    /// OpenAPI-spec-based Twilio clients plus the notification service.
    /// </summary>
    public static IServiceCollection AddTwilioOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        var settings = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio:AccountSid and Twilio:AuthToken must be configured (via user-secrets or environment variables).");
        }

        services.AddHttpClient<TwilioMessagingClient>();
        services.AddHttpClient<TwilioLookupClient>();
        services.AddScoped<ITextMessageProvider, TwilioTextMessageProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
