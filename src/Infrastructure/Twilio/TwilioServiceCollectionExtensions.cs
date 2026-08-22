using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient<IPhoneNumberLookup, TwilioLookupClient>((sp, client) =>
        {
            client.BaseAddress = new Uri(TwilioLookupClient.DefaultBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<ISmsGateway, TwilioMessagingClient>((sp, client) =>
        {
            var options = configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? TwilioMessagingClient.DefaultBaseUrl
                : options.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
