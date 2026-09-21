using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public static class ConfigureNotificationServices
{
    private const string TwilioHttpClientName = "TwilioSdk";

    /// <summary>
    /// Registers the SMS order-notification stack: validated Twilio settings, a single long-lived
    /// <see cref="TwilioSdkClient"/>, the gateway, and the application services.
    /// </summary>
    public static IServiceCollection AddSmsOrderNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start if a required credential is missing or blank.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One HttpClient with a hard per-attempt backstop and a recycling connection pool.
        services.AddHttpClient(TwilioHttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20); // per attempt backstop
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived SDK client (options built once at registration; rotation needs a restart).
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(TwilioHttpClientName);
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Explicit LoggerFactory + body logging OFF so the TWILIOCLIENT_LOG env var can never
                // switch unredacted request bodies (which carry the phone number) on from outside.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                // Per-attempt bound; the whole-call budget is enforced by the caller's CancellationToken.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Twilio:BaseUrl overrides ONLY the messaging API (server group Default = api.twilio.com).
            // The Lookup API (group Default4) keeps its own host and is deliberately left untouched.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderMessagingService, OrderMessagingService>();

        return services;
    }
}
