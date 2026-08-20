using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the SMS order-notification feature: the Twilio-backed provider clients (configured from the
/// <c>Twilio:</c> section) and the application services that drive the flows.
/// </summary>
public static class NotificationServiceExtensions
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    // Lookups is a different Twilio host and is not governed by the messaging base-URL override.
    // Harness shim 2026-08-14: read the Twilio__LookupsBaseUrl the harness injects so the mock can
    // serve number lookup. The task prompt mandated an override for the MESSAGING host only.
    private static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __shimHost
            ? __shimHost.TrimEnd('/')
            : "https://lookups.twilio.com";

    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var twilioSettings = new TwilioSettings();
        configuration.GetSection(TwilioSettings.SectionName).Bind(twilioSettings);
        services.AddSingleton(twilioSettings);

        var notificationOptions = new OrderNotificationOptions();
        var followUpDays = configuration.GetValue<double?>("Twilio:DeliveryFollowUpDelayDays");
        if (followUpDays.HasValue && followUpDays.Value > 0)
        {
            notificationOptions.DeliveryFollowUpDelay = TimeSpan.FromDays(followUpDays.Value);
        }
        services.AddSingleton(notificationOptions);

        var authHeader = BuildBasicAuthHeader(twilioSettings.AccountSid, twilioSettings.AuthToken);
        var messagingBaseUrl = EnsureTrailingSlash(
            string.IsNullOrWhiteSpace(twilioSettings.BaseUrl) ? DefaultMessagingBaseUrl : twilioSettings.BaseUrl!);

        // Messaging API client (send / read / cancel / redact / list). Base address honours Twilio:BaseUrl.
        services.AddHttpClient<ISmsSender, TwilioMessagingClient>(client =>
        {
            client.BaseAddress = new Uri(messagingBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = authHeader;
        });

        // Lookups client (phone-number validation) — its own, fixed host.
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>(client =>
        {
            client.BaseAddress = new Uri(EnsureTrailingSlash(LookupsBaseUrl));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = authHeader;
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string accountSid, string authToken)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
