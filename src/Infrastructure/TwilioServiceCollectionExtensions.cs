using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<ITwilioLookupClient, TwilioLookupClient>(client =>
        {
            client.BaseAddress = new System.Uri("https://lookups.twilio.com");
            client.Timeout = System.TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<ITwilioMessagingClient, TwilioMessagingClient>(client =>
        {
            client.Timeout = System.TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
