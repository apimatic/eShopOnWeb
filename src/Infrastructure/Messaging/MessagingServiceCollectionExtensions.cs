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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private const string TwilioHttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio-backed SMS notification stack: the fail-fast <see cref="TwilioSettings"/> binding,
    /// a long-lived <see cref="TwilioSdkClient"/> singleton, the <see cref="ISmsProvider"/> adapter, and the
    /// contact-number / order-notification services.
    /// </summary>
    public static IServiceCollection AddSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: refuse to start if any required credential is missing OR blank (each part checked separately).
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // A named HttpClient keeps this pipeline off the shared default client. PooledConnectionLifetime keeps
        // DNS fresh behind the long-lived singleton; Timeout is a per-attempt backstop.
        services.AddHttpClient(TwilioHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // One long-lived client. Options are built once here and captured — a rotated auth token takes effect
        // on process restart.
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
                // Per-attempt timeout; the adapter also imposes a whole-call deadline via a linked token.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                // LoggerFactory is set explicitly so the TWILIOSDKCLIENT_LOG env var cannot switch on body
                // logging from outside the code; request bodies (which carry the number and message text) are
                // never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // BaseUrl overrides ONLY the messaging node (Default group, api.twilio.com). Other hosts such as
            // Lookups (Default4) keep their own base addresses.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
