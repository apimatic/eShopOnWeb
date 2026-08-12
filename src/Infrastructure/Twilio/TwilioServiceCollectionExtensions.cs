using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio SDK client and the messaging gateway. The client is built over a named,
    /// factory-owned <see cref="HttpClient"/> (bounded timeout + rotated connections) so its pipeline is
    /// not shared with the rest of the app. The <c>Twilio:BaseUrl</c> override is applied to the messaging
    /// (api) server node only; the lookup node keeps its own default host.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, TwilioSettings settings)
    {
        services.AddSingleton(settings);

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds a single attempt (backstop for a hung provider). The gateway adds a total call budget.
                c.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client is a singleton, so keep DNS/connections fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // A non-idempotent send must not be transport-retried into a duplicate charge; hold the
                // pipeline at the floor (retries cannot be fully disabled) while keeping a sane per-attempt timeout.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Messaging (api) node only — lookup traffic (Default4) is deliberately left untouched.
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ITwilioMessagingGateway, TwilioMessagingGateway>();

        return services;
    }
}
