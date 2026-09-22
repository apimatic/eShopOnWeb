using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio messaging integration: fail-fast settings binding, a long-lived SDK
    /// client over a named <see cref="IHttpClientFactory"/> client, the messaging wrapper, and the
    /// notification orchestration service.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start if any credential is missing or blank (each checked separately —
        // a blank part is not a missing one). BaseUrl is an optional override and is not validated.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // A long-lived HttpClient with a per-attempt timeout backstop and pooled-connection recycling
        // (so a singleton SDK client does not cache DNS forever).
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client: built once at registration and captured in the singleton (a rotated secret
        // takes effect on process restart). LoggerFactory is set explicitly so the SDK's log
        // environment variable cannot switch unredacted body logging on from outside the code.
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
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Twilio:BaseUrl overrides ONLY the messaging API host (server group Default); Lookup
            // (group Default4) keeps its own host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ITwilioMessagingClient>(sp => new TwilioMessagingClient(
            sp.GetRequiredService<TwilioSdkClient>(),
            sp.GetRequiredService<IOptions<TwilioSettings>>().Value,
            // Build the logger from the singleton ILoggerFactory — IAppLogger<> is registered scoped,
            // and this client is a singleton, so it must not capture a scoped dependency.
            new LoggerAdapter<TwilioMessagingClient>(sp.GetRequiredService<ILoggerFactory>())));

        services.AddScoped<ISmsNotificationService, SmsNotificationService>();

        return services;
    }
}
