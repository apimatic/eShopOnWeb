using System;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupsClientName = "TwilioLookups";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);

        services.AddHttpClient(MessagingClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = TwilioUri.MessagingBaseAddress(settings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).RemoveAllLoggers();

        services.AddHttpClient<ITwilioLookupClient, TwilioLookupClient>(client =>
        {
            client.BaseAddress = new Uri("https://lookups.twilio.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).RemoveAllLoggers();

        services.AddScoped<ITwilioMessagingClient, TwilioMessagingClient>();
        return services;
    }
}
