using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public static class TwilioServiceCollectionExtensions
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    // The Lookup API lives on its own host and is never governed by the messaging base-url override.
    private const string LookupBaseUrl = "https://lookups.twilio.com/";

    /// <summary>
    /// Registers the Twilio-backed SMS provider and the order-notification service, plus the two
    /// named HTTP clients they use. Expects <see cref="TwilioSettings"/> to have been bound already.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services)
    {
        services.AddHttpClient(TwilioSmsProvider.MessagingClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultMessagingBaseUrl : settings.BaseUrl!;
            client.BaseAddress = new Uri(EnsureTrailingSlash(baseUrl));
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
        });

        services.AddHttpClient(TwilioSmsProvider.LookupClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(LookupBaseUrl);
            client.DefaultRequestHeaders.Authorization = BasicAuth(settings);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static AuthenticationHeaderValue BasicAuth(TwilioSettings settings)
    {
        // HTTP Basic: Account SID as username, Auth Token as password. The token stays in memory
        // only — it is never logged.
        var raw = $"{settings.AccountSid}:{settings.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
