using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceExtensions
{
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddSingleton<ITwilioSettingsAccessor, TwilioSettingsAccessor>();
        services.AddHttpClient(TwilioMessagingClient.HttpClientName, client =>
        {
            client.Timeout = System.TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient(TwilioLookupClient.HttpClientName, client =>
        {
            client.BaseAddress = new System.Uri(TwilioLookupClient.DefaultBaseUrl);
            client.Timeout = System.TimeSpan.FromSeconds(30);
        });
        services.AddTransient<ITwilioMessagingClient, TwilioMessagingClient>();
        services.AddTransient<ITwilioLookupClient, TwilioLookupClient>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<ICatalogOrderService, CatalogOrderService>();
        services.AddScoped<IOrderLifecycleService, OrderLifecycleService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        return services;
    }
}
