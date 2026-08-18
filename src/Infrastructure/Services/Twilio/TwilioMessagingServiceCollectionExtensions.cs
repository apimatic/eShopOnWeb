using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Binds <c>Twilio:</c> settings and registers the messaging gateway and phone-number lookup as
    /// typed HttpClients. The messaging client honours <c>Twilio:BaseUrl</c>; lookup always uses its
    /// own host. The auth token is only ever placed on an outgoing Authorization header.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = new TwilioSettings();
        configuration.GetSection(TwilioSettings.SectionName).Bind(settings);
        services.AddSingleton(settings);

        var authHeader = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>(client =>
        {
            client.BaseAddress = new Uri(settings.MessagingBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = authHeader;
        });

        services.AddHttpClient<IPhoneNumberLookup, TwilioPhoneNumberLookup>(client =>
        {
            client.BaseAddress = new Uri(TwilioSettings.LookupBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = authHeader;
        });

        return services;
    }
}
