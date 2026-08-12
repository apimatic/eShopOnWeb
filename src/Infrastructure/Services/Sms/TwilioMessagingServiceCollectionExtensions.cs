using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

public static class TwilioMessagingServiceCollectionExtensions
{
    private const string TwilioSection = "Twilio";
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Binds the <c>Twilio:</c> configuration section and registers the Twilio SDK client and the
    /// <see cref="ISmsGateway"/> over it.
    ///
    /// The client is built over a named <see cref="HttpClient"/> (not the shared default one) so its
    /// timeout, primary handler and retry policy are scoped to this integration. Because an SMS send is a
    /// non-idempotent POST and the SDK retries transport failures on every verb, retries are held to the
    /// minimum the pipeline allows, and a pooled-connection lifetime keeps DNS fresh behind the long-lived
    /// singleton client. <c>Twilio:BaseUrl</c>, when set, overrides the messaging-API base address only.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSection));

        services.AddHttpClient(HttpClientName, c =>
            {
                // Bounds a single attempt against a hung provider (default is 100s).
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so IHttpClientFactory handler rotation never
                // reaches it — refresh pooled connections (and DNS) ourselves.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Keep write retries to the pipeline's floor so a transport reset cannot re-send an SMS
                // more than once, and cap a single attempt well under the default.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Twilio:BaseUrl governs the messaging ("api") server only — never the Lookup host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        return services;
    }
}
