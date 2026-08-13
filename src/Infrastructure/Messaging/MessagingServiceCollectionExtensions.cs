using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Wires up the Twilio-backed SMS notification integration: settings bound from the <c>Twilio:</c> section, the
/// typed HTTP clients for the messaging and Lookups APIs (each with Basic auth and the correct base address),
/// and the order-notification orchestration service.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        // Messaging API (send / read / cancel / redact / reconcile). Honors the Twilio:BaseUrl override.
        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(settings.EffectiveMessagingBaseUrl);
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
        });

        // Lookups API (number validation / canonicalization). Served from its own host, not the messaging base.
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(TwilioSettings.LookupsBaseUrl);
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
        });

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static AuthenticationHeaderValue BasicAuth(TwilioSettings settings)
    {
        var raw = $"{settings.AccountSid}:{settings.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }
}
