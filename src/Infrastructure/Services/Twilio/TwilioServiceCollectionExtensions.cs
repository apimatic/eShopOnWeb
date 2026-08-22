using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddTransient<TwilioBasicAuthHandler>();

        services.AddHttpClient(TwilioLookupClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(TwilioLookupClient.DefaultBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddHttpClient(TwilioMessagingClient.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                    ? TwilioMessagingClient.DefaultBaseUrl
                    : options.BaseUrl.Trim();
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<TwilioBasicAuthHandler>();

        services.AddScoped<IPhoneNumberLookup, TwilioLookupClient>();
        services.AddScoped<ISmsGateway, TwilioMessagingClient>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
