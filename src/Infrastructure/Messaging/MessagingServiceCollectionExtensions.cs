using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twilio-backed SMS gateway, phone-number validator and the order-notification orchestrator.
    /// Settings are bound from the <c>Twilio</c> configuration section using exactly the documented keys; no
    /// value is hard-coded. HTTP client logging is removed so that request URIs (which can carry a shopper's
    /// number, e.g. Lookup) never reach the logs.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is required.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is required.");

        services.AddHttpClient<ISmsGateway, TwilioMessagingService>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(settings.ResolveMessagingBaseUrl().TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers();

        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(TwilioSettings.LookupBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers();

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static AuthenticationHeaderValue BasicAuth(TwilioSettings settings) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
}
