using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Twilio-backed notification capability: settings from the "Twilio"
    /// configuration section, an authenticated HttpClient (Basic auth per the
    /// spec's accountSid_authToken security scheme), and the services built on it.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        services.AddHttpClient(TwilioSmsGateway.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IPhoneNumberValidator, TwilioPhoneNumberValidator>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
