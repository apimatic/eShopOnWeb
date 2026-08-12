using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    /// <summary>
    /// Wire up the Twilio SMS integration: bind <c>Twilio:</c> settings, construct a single long-lived
    /// <see cref="TwilioSdkClient"/>, and register the <see cref="ISmsGateway"/> over it.
    /// </summary>
    public static IServiceCollection AddTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        // A named HttpClient keeps the timeout and connection lifetime scoped to Twilio, off the shared
        // default client. Timeout bounds a single attempt; PooledConnectionLifetime keeps DNS fresh behind
        // the long-lived (singleton) client.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20))
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
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid ?? string.Empty,
                    Password = settings.AuthToken ?? string.Empty
                },
                // Sending an SMS is a non-idempotent, billable write. A transport-level failure is retried on
                // every verb regardless of method, so we hold retries at the floor (MaxRetries = 1) to minimise
                // the chance of the provider receiving the same message twice, and bound each attempt.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Optional verbatim override of the messaging (api node) base address. Untouched when unset.
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
