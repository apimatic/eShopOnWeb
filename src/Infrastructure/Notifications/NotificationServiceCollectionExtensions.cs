using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    public static IServiceCollection AddOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient<ISmsProvider, TwilioSmsProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            // BaseUrl, when set, is used verbatim as the base address for every messaging-API call.
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(options.BaseUrl) ? DefaultMessagingBaseUrl : options.BaseUrl,
                UriKind.Absolute);
            client.DefaultRequestHeaders.Authorization = BasicAuth(options);
        });

        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            // Lookup is a separate Twilio capability on its own host; Twilio:BaseUrl does not govern it.
            client.BaseAddress = new Uri(LookupBaseUrl, UriKind.Absolute);
            client.DefaultRequestHeaders.Authorization = BasicAuth(options);
        });

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static AuthenticationHeaderValue BasicAuth(TwilioOptions options)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
