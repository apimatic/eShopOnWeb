using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient(TwilioMessagingClient.MessagingClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient(TwilioMessagingClient.LookupClientName, client =>
        {
            client.BaseAddress = new Uri(TwilioMessagingClient.LookupBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<TwilioMessagingClient>();
        services.AddScoped<ISmsGateway>(sp => sp.GetRequiredService<TwilioMessagingClient>());
        services.AddScoped<IPhoneNumberLookupService>(sp => sp.GetRequiredService<TwilioMessagingClient>());
        return services;
    }
}
