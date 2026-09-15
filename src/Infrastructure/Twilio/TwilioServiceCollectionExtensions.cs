using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the Twilio-backed SMS notification integration: binds <c>Twilio:</c> settings, registers
    /// the hand-written provider clients (with HTTP Basic auth and no request-logging so a number in a
    /// request URL is never logged), and registers the gateway, validator and application services.
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient<TwilioMessagingClient>(ConfigureAuth)
            .RemoveAllLoggers();
        services.AddHttpClient<TwilioLookupClient>(ConfigureAuth)
            .RemoveAllLoggers();

        // Provider-facing abstractions.
        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IPhoneNumberValidator, TwilioPhoneNumberValidator>();

        // Application services that orchestrate the notification flows.
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
        services.AddScoped<ISmsNotificationService, SmsNotificationService>();

        return services;
    }

    private static void ConfigureAuth(IServiceProvider serviceProvider, System.Net.Http.HttpClient http)
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TwilioSettings>>().Value;
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        http.Timeout = TimeSpan.FromSeconds(30);
    }
}
