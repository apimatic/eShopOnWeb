using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Wires up the SMS order-notification feature: the Twilio-backed provider clients (bound from the
/// <c>Twilio:</c> configuration section) and the application services that drive the flows.
/// </summary>
public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new TwilioOptions
        {
            AccountSid = configuration["Twilio:AccountSid"] ?? string.Empty,
            AuthToken = configuration["Twilio:AuthToken"] ?? string.Empty,
            FromNumber = configuration["Twilio:FromNumber"] ?? string.Empty,
            MessagingServiceSid = configuration["Twilio:MessagingServiceSid"] ?? string.Empty,
            BaseUrl = configuration["Twilio:BaseUrl"]
        };

        services.AddSingleton(options);

        // Messaging API — base address honours the optional Twilio:BaseUrl override.
        services.AddHttpClient<ISmsProvider, TwilioMessagingClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<TwilioOptions>();
            ConfigureClient(client, o.EffectiveMessagingBaseUrl, o);
        })
        // Remove the default HttpClient logging so request URIs (which for other Twilio calls can carry
        // personal data) are never written to logs.
        .RemoveAllLoggers();

        // Lookups API — always the provider's lookups host; the Twilio:BaseUrl override does not apply here.
        services.AddHttpClient<IPhoneNumberValidator, TwilioLookupsClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<TwilioOptions>();
            ConfigureClient(client, TwilioOptions.LookupsBaseUrl, o);
        })
        .RemoveAllLoggers();

        // Application services that make up the feature.
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IShopperContactService, ShopperContactService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationReconciliationService, NotificationReconciliationService>();

        return services;
    }

    private static void ConfigureClient(System.Net.Http.HttpClient client, string baseUrl, TwilioOptions options)
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}")));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Twilio/1.0");
    }
}
