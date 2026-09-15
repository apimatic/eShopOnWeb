using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class TwilioServiceCollectionExtensions
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(options =>
        {
            configuration.GetSection(TwilioOptions.SectionName).Bind(options);
            Overlay(configuration, "TWILIO_ACCOUNT_SID", value => options.AccountSid = value);
            Overlay(configuration, "TWILIO_AUTH_TOKEN", value => options.AuthToken = value);
            Overlay(configuration, "TWILIO_FROM_NUMBER", value => options.FromNumber = value);
            Overlay(configuration, "TWILIO_MESSAGING_SERVICE_SID", value => options.MessagingServiceSid = value);
        });

        services.AddHttpClient(TwilioMessagingClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwilioOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? DefaultMessagingBaseUrl : options.BaseUrl;
            client.BaseAddress = new Uri(EnsureTrailingSlash(baseUrl));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddHttpClient(TwilioLookupClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(EnsureTrailingSlash(LookupsBaseUrl));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<ISmsMessagingClient, TwilioMessagingClient>();
        services.AddScoped<IPhoneNumberLookupClient, TwilioLookupClient>();
        return services;
    }

    private static void Overlay(IConfiguration configuration, string envKey, Action<string> assign)
    {
        var value = configuration[envKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            assign(value);
        }
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
