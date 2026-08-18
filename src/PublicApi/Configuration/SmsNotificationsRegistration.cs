using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Wires up the SMS order-notification capability: the Twilio-backed provider/validator clients
/// (hand-written against the OpenAPI specs) and the application services that orchestrate them.
/// </summary>
public static class SmsNotificationsRegistration
{
    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind the Twilio settings from the `Twilio:` configuration section (env / user-secrets).
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        // Messaging API client — subject to the Twilio:BaseUrl override.
        services.AddHttpClient<ISmsProvider, TwilioSmsProvider>(ConfigureBasicAuth);

        // Lookups API client — always the provider's own Lookups host, never the messaging override.
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>(ConfigureBasicAuth);

        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<INotificationOperationsService, NotificationOperationsService>();

        return services;
    }

    private static void ConfigureBasicAuth(IServiceProvider serviceProvider, System.Net.Http.HttpClient httpClient)
    {
        var options = serviceProvider.GetRequiredService<IOptions<TwilioOptions>>().Value;

        // HTTP Basic auth (AccountSid:AuthToken) as declared by the specs. The auth token is used only
        // to build this header; it is never logged, returned, or written to a source file.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    }
}
