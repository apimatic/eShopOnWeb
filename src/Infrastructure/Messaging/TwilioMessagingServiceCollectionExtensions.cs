using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Registers the Twilio messaging integration: settings (validated non-blank at startup), the SDK
/// client over a long-lived pooled <see cref="HttpClient"/>, and the gateway/validator/services.
/// </summary>
public static class TwilioMessagingServiceCollectionExtensions
{
    private const string HttpClientName = "twilio-messaging";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + fail-fast. Every credential part is checked separately (blank ≠ missing), at startup.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // Long-lived pooled HttpClient; per-attempt backstop timeout + connection recycling for DNS.
        services.AddHttpClient(HttpClientName, (sp, c) =>
            {
                var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
                c.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.CallTimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
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
                // Set the logger factory explicitly so the TWILIOCLIENT_LOG env var cannot switch on
                // body logging from outside the code; request bodies (which carry the number + text)
                // are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.CallTimeoutSeconds)) }
            };

            // Messaging-API base URL override (governs the Default server group only; not number-lookup).
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;

            return new TwilioSdkClient(httpClient, options);
        });

        // The SDK client is a singleton (long-lived HttpClient + pipelines); the thin gateway/validator
        // are scoped so they can consume the app's scoped IAppLogger without a captive dependency.
        services.AddScoped<ITwilioMessagingGateway, TwilioMessagingGateway>();
        services.AddScoped<IPhoneNumberValidator, TwilioPhoneNumberValidator>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
