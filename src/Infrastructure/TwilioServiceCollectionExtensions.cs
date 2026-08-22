using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddOrderSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddTransient<TwilioBasicAuthHandler>();

        services.AddHttpClient<IPhoneNumberLookupClient, TwilioLookupClient>(client =>
            {
                client.BaseAddress = new System.Uri("https://lookups.twilio.com/");
            })
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddHttpClient<ITwilioMessagingClient, TwilioMessagingClient>()
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddScoped<OrderSmsNotifier>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IShopperOrderService, ShopperOrderService>();
        services.AddScoped<IOperatorOrderService, OperatorOrderService>();
        services.AddScoped<IOperatorNotificationService, OperatorNotificationService>();

        return services;
    }

    public static IConfigurationBuilder AddTwilioEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var mapped = new Dictionary<string, string?>();
        Map(mapped, "TWILIO_ACCOUNT_SID", "Twilio:AccountSid");
        Map(mapped, "TWILIO_AUTH_TOKEN", "Twilio:AuthToken");
        Map(mapped, "TWILIO_FROM_NUMBER", "Twilio:FromNumber");
        Map(mapped, "TWILIO_MESSAGING_SERVICE_SID", "Twilio:MessagingServiceSid");
        Map(mapped, "TWILIO_BASE_URL", "Twilio:BaseUrl");

        if (mapped.Count > 0)
        {
            configuration.AddInMemoryCollection(mapped);
        }

        return configuration;
    }

    private static void Map(IDictionary<string, string?> mapped, string environmentVariable, string configurationKey)
    {
        var value = System.Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            mapped[configurationKey] = value;
        }
    }
}
