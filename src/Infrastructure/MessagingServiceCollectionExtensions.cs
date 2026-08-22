using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient<ISmsGateway, TwilioSmsGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            client.BaseAddress = new Uri(TwilioSmsGateway.NormalizeBaseUrl(options.BaseUrl));
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IPhoneNumberLookupClient, TwilioLookupClient>((_, client) =>
        {
            client.BaseAddress = new Uri(TwilioLookupClient.DefaultBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<IPublicApiOrderService, PublicApiOrderService>();

        return services;
    }
}
