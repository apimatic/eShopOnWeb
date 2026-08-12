using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the SMS order-notification capability: the Twilio options, the two HTTP clients (messaging
/// and lookup) and the application services. Request logging is removed from both clients so that a
/// shopper's number — which appears in the lookup URL — never reaches a log.
/// </summary>
public static class SmsNotificationRegistration
{
    private const string TwilioLookupBaseUrl = "https://lookups.twilio.com/";

    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        // Messaging API: honours the configured (overridable) base URL.
        services.AddHttpClient<ISmsSender, TwilioMessagingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var baseUrl = options.ResolvedMessagingBaseUrl;
            if (!baseUrl.EndsWith('/'))
            {
                baseUrl += "/";
            }
            client.BaseAddress = new Uri(baseUrl);
            ApplyBasicAuth(client, options);
        }).RemoveAllLoggers();

        // Lookup API: a different host, deliberately NOT governed by the messaging base-URL override.
        services.AddHttpClient<IPhoneNumberValidator, TwilioLookupClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            client.BaseAddress = new Uri(TwilioLookupBaseUrl);
            ApplyBasicAuth(client, options);
        }).RemoveAllLoggers();

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IOrderFulfillmentService, OrderFulfillmentService>();
        services.AddScoped<IOrderQueryService, OrderQueryService>();
        services.AddScoped<INotificationAdminService, NotificationAdminService>();

        return services;
    }

    private static void ApplyBasicAuth(HttpClient client, TwilioOptions options)
    {
        // Setting the auth header here (not in request-building code) keeps the token out of that code.
        var raw = Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }
}
