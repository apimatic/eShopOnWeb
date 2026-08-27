using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class TwilioServiceCollectionExtensions
{
    /// <summary>
    /// Binds the Twilio: configuration section and registers the messaging client, the phone
    /// number validator and the order notification orchestration. Credentials come from
    /// user-secrets/environment variables and are only ever placed on the Authorization header.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName));

        services.AddHttpClient<ISmsMessagingClient, TwilioMessagingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            ApplyBasicAuth(client, options);
        });

        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            ApplyBasicAuth(client, options);
        });

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }

    private static void ApplyBasicAuth(HttpClient client, TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            return; // validated when the client is first used
        }
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
