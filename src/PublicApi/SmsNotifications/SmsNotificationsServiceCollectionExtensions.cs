using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// Wires up the SMS order-notification feature: Twilio settings, the spec-built gateway (with HTTP
/// Basic auth), and the application services that orchestrate contact numbers and notifications.
/// </summary>
public static class SmsNotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ISmsGateway, TwilioMessagingGateway>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TwilioSettings>>().Value;

            // HTTP Basic (AccountSid:AuthToken) per the spec's accountSid_authToken scheme. The auth
            // token is set on the client here and is never logged or otherwise surfaced.
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        // The Lookups request URL carries the shopper's number in its path; strip the default HTTP
        // client logging entirely so a number can never reach the logs regardless of log level.
        .RemoveAllLoggers();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationOperationsService, NotificationOperationsService>();

        return services;
    }
}
