using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

public static class TwilioRegistration
{
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Missing credentials are a deployment fault: fail at startup, not on the first request.
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A named client keeps this pipeline (timeout, handler lifetime) off the shared default client.
        services.AddHttpClient(TwilioMessaging.HttpClientName, client =>
            {
                // Bounds one attempt; the whole-call budget lives in TwilioMessaging.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton: keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var twilioOptions = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioMessaging.HttpClientName);

            var clientOptions = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = twilioOptions.AccountSid,
                    Password = twilioOptions.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(twilioOptions.BaseUrl))
            {
                // Messaging API only (server "Default" = api.twilio.com). Lookup and other
                // Twilio hosts are deliberately left on their defaults.
                clientOptions.Server.Default.Production.BaseUrl = twilioOptions.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<ITwilioMessaging, TwilioMessaging>();

        return services;
    }
}
