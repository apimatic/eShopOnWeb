using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "twilio";

    /// <summary>
    /// Registers the Twilio messaging integration: fail-fast options, the SDK client (over a named,
    /// long-lived <see cref="HttpClient"/>), the gateway, and the notification/contact services.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast: bind the Twilio section and refuse to start if any credential is missing or blank.
        // ValidateOnStart makes it a startup failure, not a first-request 401.
        // Every part is checked non-blank (a blank part is not a missing one). The message names the
        // config key an operator must set; it never echoes the value (the auth token is a secret).
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // A named HttpClient keeps this pipeline off the shared default client. PooledConnectionLifetime
        // recycles connections behind the long-lived (singleton) SDK client so DNS changes are picked up;
        // Timeout is a per-attempt backstop (the whole-call budget is the CancellationToken in the gateway).
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = options.AccountSid,
                    Password = options.AuthToken
                },
                // LoggerFactory set explicitly so the SDK's TWILIOCLIENT_LOG env var cannot switch body
                // logging on from outside the code; request bodies (which carry the destination number and
                // message text) are never logged.
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            // Twilio:BaseUrl overrides the MESSAGING host only (server group Default = api.twilio.com), used
            // verbatim for send/read/reconcile. Lookups (a different host/group) is deliberately left alone.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Production.BaseUrl = options.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<ISmsSender, TwilioMessagingGateway>();
        services.AddScoped<IContactNumberService, ContactNumberService>();
        services.AddScoped<IOrderNotificationService, OrderNotificationService>();

        return services;
    }
}
