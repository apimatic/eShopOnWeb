using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public static class TwilioServiceCollectionExtensions
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));
        services.AddSingleton<ITwilioSendingNumberAccessor, TwilioSendingNumberAccessor>();
        services.AddScoped<IPhoneNumberLookup, TwilioPhoneNumberLookup>();
        services.AddScoped<ISmsMessageGateway, TwilioSmsMessageGateway>();

        services.AddHttpClient(TwilioSmsMessageGateway.HttpClientName, (sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? "https://api.twilio.com"
                    : settings.BaseUrl.Trim();
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .RemoveAllLoggers();

        services.AddHttpClient(TwilioPhoneNumberLookup.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://lookups.twilio.com");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .RemoveAllLoggers();

        return services;
    }
}
