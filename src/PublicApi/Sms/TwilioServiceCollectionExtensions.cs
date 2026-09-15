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

namespace Microsoft.eShopWeb.PublicApi.Sms;

public static class TwilioServiceCollectionExtensions
{
    private const string TwilioHttpClientName = "TwilioSdk";

    /// <summary>
    /// Registers the Twilio messaging integration: validated settings, the SDK client over an
    /// isolated named <see cref="HttpClient"/>, and the <see cref="ISmsGateway"/> implementation.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // A missing credential is a deployment fault, not a request fault: validate on start and
        // refuse to boot rather than surfacing a 401 on the first unlucky request.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Keep this pipeline off the shared default client: its own timeout (bounds one attempt),
        // the single-send guard, and a pooled-connection lifetime so DNS stays fresh behind the
        // long-lived (singleton) client below.
        services.AddTransient<SingleSendGuardHandler>();
        services.AddHttpClient(TwilioHttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddHttpMessageHandler<SingleSendGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioHttpClientName);

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Per attempt; the whole-call budget lives on a linked token in the gateway.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Messaging-API base-URL override ONLY (config key Twilio:BaseUrl). The Lookup host
            // (Server.Default4) is deliberately left at its provider default.
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
