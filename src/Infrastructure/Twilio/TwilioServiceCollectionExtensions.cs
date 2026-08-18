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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioServiceCollectionExtensions
{
    private const string TwilioHttpClientName = "TwilioMessaging";

    /// <summary>
    /// Registers the Twilio-backed <see cref="ISmsProvider"/>: binds and validates <see cref="TwilioSettings"/>
    /// at startup, builds a long-lived <see cref="TwilioSdkClient"/> over an <see cref="IHttpClientFactory"/>
    /// client (bounded timeout + connection recycling), applies the messaging base-URL override when present,
    /// and wires the provider.
    /// </summary>
    public static IServiceCollection AddTwilioSmsProvider(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail startup (not the first request) if a required credential is missing. Each message names the
        // configuration key that is unset and never echoes any value.
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetSection(TwilioSettings.ConfigurationSection))
            .Validate(s => !string.IsNullOrWhiteSpace(s.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .ValidateOnStart();

        // Bounded per-attempt timeout + pooled-connection recycling for the long-lived (singleton) client.
        services.AddHttpClient(TwilioHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
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
                // A message send is a non-idempotent POST; keep retries at the floor to minimise the chance a
                // transport-level retry sends a duplicate.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(30)
                }
            };

            // Messaging base-URL override, only when configured, and only for the messaging ("Default") server.
            // The lookup server ("Default4") is left at its own host.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();

        return services;
    }
}
