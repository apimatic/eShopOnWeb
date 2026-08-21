using System;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddSingleton<ITwilioConfiguration, TwilioConfigurationAdapter>();

        services.AddHttpClient(TwilioHttp.LookupsClientName, client =>
        {
            client.BaseAddress = new Uri(TwilioHttp.DefaultLookupsBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).RemoveAllLoggers();

        services.AddHttpClient(TwilioHttp.MessagingClientName, (sp, client) =>
        {
            var settings = new TwilioSettings
            {
                BaseUrl = configuration["Twilio:BaseUrl"]
            };
            var baseUrl = TwilioHttp.MessagingBaseUrl(settings);
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).RemoveAllLoggers();

        services.AddTransient<IPhoneNumberLookup, TwilioPhoneNumberLookup>();
        services.AddTransient<ISmsGateway, TwilioSmsGateway>();
        return services;
    }
}
