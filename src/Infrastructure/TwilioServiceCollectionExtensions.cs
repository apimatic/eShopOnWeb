using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ApplyEnvironmentOverrides(configuration);

        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddHttpClient(TwilioMessagingClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient(TwilioLookupClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ISmsGateway, TwilioMessagingClient>();
        services.AddScoped<IPhoneNumberLookup, TwilioLookupClient>();

        return services;
    }

    public static void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        var overrides = new Dictionary<string, string?>();
        AddIfPresent(overrides, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        AddIfPresent(overrides, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        AddIfPresent(overrides, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        AddIfPresent(overrides, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        AddIfPresent(overrides, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (overrides.Count == 0)
        {
            return;
        }

        if (configuration is IConfigurationBuilder builder)
        {
            builder.AddInMemoryCollection(overrides);
        }
    }

    private static void AddIfPresent(IDictionary<string, string?> overrides, string environmentVariable, string configurationKey)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[configurationKey] = value;
        }
    }
}
