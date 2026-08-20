using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class TwilioNotificationServiceExtensions
{
    private const string TwilioHttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio messaging client, the SMS gateway, and the order-notification service.
    /// Credentials are bound from the <c>Twilio:</c> configuration section and validated at startup, so a
    /// misconfigured deployment refuses to boot instead of failing on the first shopper's request.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.CONFIG_NAME))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The single-send guard runs inside the messaging HttpClient pipeline so a transport-failure retry
        // cannot duplicate a create-message POST.
        services.AddTransient<SingleSendGuardHandler>();

        services.AddHttpClient(TwilioHttpClientName, (sp, http) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                // A backstop bound on a single attempt (the SDK's per-attempt Retry.Timeout is set below too).
                http.Timeout = TimeSpan.FromSeconds(settings.PerAttemptTimeoutSeconds + 5);
            })
            .AddHttpMessageHandler<SingleSendGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton, so keep DNS fresh behind it.
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
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(settings.PerAttemptTimeoutSeconds)
                }
            };

            // Apply the optional base-URL override to the MESSAGING host only. The lookups host (Default4) is
            // a different server and must keep its own default — never point it at the messaging base URL.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            // Twilio__LookupsBaseUrl overrides ONLY the Lookup host, verbatim.
            var lookupsBaseUrl = System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl");
            if (!string.IsNullOrEmpty(lookupsBaseUrl))
                options.Server.Default4.Production.BaseUrl = lookupsBaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
