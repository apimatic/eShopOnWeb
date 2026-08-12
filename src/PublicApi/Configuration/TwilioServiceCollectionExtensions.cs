using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS integration: settings bound from the "Twilio" section, the
    /// messaging and lookups clients (each a typed HttpClient with Basic auth and its own base URL),
    /// and the order-notification orchestration service.
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.ConfigSection));

        // Messaging API — base URL honours the Twilio:BaseUrl override.
        services.AddHttpClient<ISmsGateway, TwilioMessagingClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                client.BaseAddress = new Uri(settings.MessagingBaseUrl);
                ConfigureBasicAuth(client, settings);
            })
            // Remove the default request/response logging so a shopper's number (in the request URI or
            // body) is never written to logs by the HTTP stack.
            .RemoveAllLoggers();

        // Lookups API — always lookups.twilio.com, deliberately not affected by Twilio:BaseUrl.
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberLookupClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                client.BaseAddress = new Uri(TwilioSettings.LookupsBaseUrl.TrimEnd('/') + "/");
                ConfigureBasicAuth(client, settings);
            })
            .RemoveAllLoggers();

        services.AddScoped<INotificationService, OrderNotificationService>();

        return services;
    }

    private static void ConfigureBasicAuth(HttpClient client, TwilioSettings settings)
    {
        var accountSid = settings.AccountSid ?? string.Empty;
        var authToken = settings.AuthToken ?? string.Empty;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
