using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Registers the Twilio-backed SMS notification integration: the provider adapter, the Twilio SDK
/// client, and the application services that drive the notification flows.
/// </summary>
public static class SmsNotificationServiceExtensions
{
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();

        // Settings bound from the Twilio: section (values come from user-secrets / environment).
        services.AddSingleton(_ => TwilioSettings.FromConfiguration(configuration));

        // A single long-lived SDK client over an IHttpClientFactory-managed handler.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<TwilioSettings>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("twilio");

            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                }
            };

            // Twilio:BaseUrl, when set, overrides the messaging API base address only.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IShopperOrderService, ShopperOrderService>();
        services.AddScoped<INotificationAdminService, NotificationAdminService>();

        return services;
    }
}
