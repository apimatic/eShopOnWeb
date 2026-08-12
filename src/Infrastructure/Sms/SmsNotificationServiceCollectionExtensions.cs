using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Wires up the Twilio-backed SMS notification feature: the provider gateway (a typed HttpClient
/// built against Twilio's OpenAPI contract) and the application services that use it. Registered only
/// by hosts that expose the feature, so other hosts are unaffected.
/// </summary>
public static class SmsNotificationServiceCollectionExtensions
{
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Validation runs when the options are first used (i.e. when the gateway is constructed to
        // serve an SMS-related request), not at host startup. This keeps the rest of the API — which
        // does not need Twilio — running even if the SMS feature is left unconfigured, while a
        // misconfigured feature still fails loudly and clearly the moment it is exercised.
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid), "Twilio:AccountSid is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken), "Twilio:AuthToken is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber), "Twilio:FromNumber is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid), "Twilio:MessagingServiceSid is required.");

        // Typed client for the provider gateway. Default HttpClient logging is removed so that request
        // URIs — which for a Lookup carry the phone number in the path — are never written to logs.
        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers();

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
