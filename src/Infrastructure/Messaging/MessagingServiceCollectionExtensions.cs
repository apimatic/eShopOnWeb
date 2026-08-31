using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>Named HttpClient for the Twilio SDK, keeping its pipeline off the shared default client.</summary>
    public const string TwilioHttpClientName = "Twilio";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Missing credentials are a deployment fault: fail at startup, not on the first request.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(TwilioHttpClientName, client =>
            {
                // Backstop for a hung provider; bounds one attempt. The per-call budget
                // lives at the messaging boundary (TwilioMessagingService).
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            // Optional override of the messaging API host only (used verbatim as the base
            // address). Lookup and other capabilities keep their own hosts.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioHttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<IMessagingService, TwilioMessagingService>();

        return services;
    }
}
