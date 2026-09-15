using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class SmsNotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS gateway (bound from the <c>Twilio:</c> configuration section)
    /// and the application services that drive contact numbers and order notifications.
    /// </summary>
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        // Typed HttpClient for the provider; the same type backs both the gateway and the
        // sender-identity abstraction the domain uses.
        services.AddHttpClient<TwilioSmsGateway>();
        services.AddTransient<ISmsGateway>(sp => sp.GetRequiredService<TwilioSmsGateway>());
        services.AddTransient<ISmsSenderIdentity>(sp => sp.GetRequiredService<TwilioSmsGateway>());

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationAdminService, NotificationAdminService>();

        return services;
    }
}
