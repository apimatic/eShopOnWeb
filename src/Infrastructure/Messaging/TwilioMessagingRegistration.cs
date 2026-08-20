using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Wires the Twilio SDK client and the messaging boundary into DI. Credentials are read from configuration
/// (never hard-coded); the app refuses to start if any required Twilio setting is missing.
///
/// The SDK client is built over a dedicated, named <see cref="System.Net.Http.HttpClient"/> whose default
/// request/response logging is removed. This is deliberate and load-bearing: the phone-number Lookup call
/// carries the shopper's number in the request URL, and <c>IHttpClientFactory</c>'s built-in logging would
/// otherwise write that URL to the logs at Information level. Removing the loggers on this one client keeps a
/// shopper's number out of the logs regardless of the environment's log levels.
/// </summary>
public static class TwilioMessagingRegistration
{
    private const string HttpClientName = "TwilioMessaging";

    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate the settings, and fail startup (not the first request) if a required value is absent.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = configuration.GetSection(TwilioSettings.SectionName).Get<TwilioSettings>() ?? new TwilioSettings();

        services.AddHttpClient(HttpClientName, client =>
            {
                // Backstop against a hung provider pinning a request thread (the CancellationToken bounds the call).
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            // Never log this client's requests — the Lookup URL contains the shopper's number.
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keep DNS fresh behind the long-lived (singleton) client below.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is lightweight wrappers over a shared HTTP pipeline — construct it once.
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new TwilioSdkClientOptions
            {
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // Bound a single attempt so a hung provider can't pin the pipeline indefinitely.
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15) }
            };

            // Messaging-API base-URL override ONLY: set the Default (api) node, leaving the lookups node alone.
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

        services.AddScoped<ITwilioMessagingService, TwilioMessagingService>();

        return services;
    }
}
