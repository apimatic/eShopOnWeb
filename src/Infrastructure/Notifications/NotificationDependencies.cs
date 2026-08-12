using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Registers the Twilio-backed SMS notification stack: settings binding, the two provider HTTP
/// clients (messaging + lookups) and the application notification service.
/// </summary>
public static class NotificationDependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.ConfigSection));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.ConfigSection));
        services.AddSingleton<INotificationOptions>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationOptions>>().Value);

        var settings = configuration.GetSection(TwilioSettings.ConfigSection).Get<TwilioSettings>() ?? new TwilioSettings();
        var basicAuth = BuildBasicAuthHeader(settings.AccountSid, settings.AuthToken);

        // Messaging client. Base address is owned by the client (honours Twilio:BaseUrl verbatim), so
        // here we only attach auth. Loggers are removed so no request URI, header or PII is ever logged.
        services.AddHttpClient<ISmsGateway, TwilioMessagingClient>(client =>
        {
            client.DefaultRequestHeaders.Authorization = basicAuth;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers();

        // Lookups client. Same auth; its host is fixed (lookups.twilio.com) and the number appears in
        // the request path, which is exactly why default logging must be off.
        services.AddHttpClient<IPhoneNumberValidationService, TwilioLookupClient>(client =>
        {
            client.DefaultRequestHeaders.Authorization = basicAuth;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).RemoveAllLoggers();

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string accountSid, string authToken)
    {
        var raw = Encoding.ASCII.GetBytes($"{accountSid}:{authToken}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }
}
