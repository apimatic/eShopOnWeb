using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "twilio";

    /// <summary>
    /// Registers the Twilio-backed SMS notification stack: validated settings (fail-fast at startup), a
    /// long-lived <see cref="TwilioSdkClient"/> over a pooled named HttpClient, the provider abstraction and
    /// the order-notification orchestration service.
    /// </summary>
    public static IServiceCollection AddTwilioNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start when any credential is missing or blank (a blank part is not a missing one).
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // Named HttpClient: per-attempt timeout set explicitly, connection pool recycled so a long-lived
        // singleton client does not pin stale DNS.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is long-lived (holds resilience pipelines + auth). Options are captured once here,
        // so a rotated secret takes effect on process restart.
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
                // The SDK's built-in request-line logger writes the request URL, and the Lookups call carries
                // the phone number in the URL *path* (which the SDK does not redact). A shopper's number must
                // never be logged, so the SDK logger is disabled outright with NullLoggerFactory — assigning it
                // explicitly also prevents the TWILIOCLIENT_LOG env var from arming logging from outside the
                // code. This application does its own logging (ids/status only, never numbers or message text).
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Optional override applies to the messaging (Messages) host only — not to Lookups (Default4).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        // Expose the resolved settings to the provider.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TwilioSettings>>().Value);

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
