using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioServiceCollectionExtensions
{
    private const string HttpClientName = "TwilioMessaging";

    // Per-attempt bound on a single provider call (the SDK Timeout is per attempt, not per call).
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);
    // Backstop on the underlying HttpClient (also per attempt).
    private static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Wires the Twilio SMS integration: binds and validates the <c>Twilio:</c> section (fail-fast — the
    /// host refuses to start when a required value is missing or blank), registers a long-lived
    /// <see cref="TwilioSdkClient"/> over a dedicated named <see cref="HttpClient"/>, and the application
    /// port <see cref="ISmsGateway"/>.
    /// </summary>
    public static IServiceCollection AddTwilioSmsNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
        ValidateOrThrow(options);

        // Capture the bound options ONCE, here — a rotated secret takes effect on process restart.
        services.AddSingleton(options);
        services.AddSingleton<IResendIdempotencyGuard, InMemoryResendIdempotencyGuard>();

        services.AddHttpClient(HttpClientName, c => c.Timeout = HttpClientTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Keep DNS fresh behind a long-lived singleton client.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = options.AccountSid,
                    Password = options.AuthToken
                },
                // Per-attempt timeout; the whole-operation budget is enforced by the caller's CancellationToken.
                Retry = RetryOptions.Default() with { Timeout = PerAttemptTimeout },
                // Silence the SDK's built-in request/response logger with an explicit NullLoggerFactory.
                // This is deliberate and load-bearing for privacy: the number-lookup call carries the
                // shopper's number in the request URL *path* (paths are not redacted), and message bodies
                // carry the number + text. Assigning the factory explicitly also disables the SDK's log
                // environment variable, so nothing external can switch body/URL logging back on. Observability
                // is provided by the gateway itself, which logs SIDs, statuses and provider error codes only —
                // never the number or the body.
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestBody = false
                }
            };

            // The optional base-URL override governs ONLY the messaging API (server group Default).
            // Number lookup is served from a different host (Default4) and is deliberately left untouched.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Production.BaseUrl = options.BaseUrl!;
            }

            return new TwilioSdkClient(httpClient, clientOptions);
        });

        services.AddScoped<ISmsGateway, TwilioSmsGateway>();

        return services;
    }

    private static void ValidateOrThrow(TwilioOptions options)
    {
        // Every required credential part checked individually — a blank part is not a missing one.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AccountSid)) missing.Add($"{TwilioOptions.SectionName}:AccountSid");
        if (string.IsNullOrWhiteSpace(options.AuthToken)) missing.Add($"{TwilioOptions.SectionName}:AuthToken");
        if (string.IsNullOrWhiteSpace(options.FromNumber)) missing.Add($"{TwilioOptions.SectionName}:FromNumber");
        if (string.IsNullOrWhiteSpace(options.MessagingServiceSid)) missing.Add($"{TwilioOptions.SectionName}:MessagingServiceSid");

        if (missing.Count > 0)
        {
            // Names only — never the values.
            throw new InvalidOperationException(
                $"Twilio SMS notifications are not configured. Missing or blank settings: {string.Join(", ", missing)}. " +
                "Provide them via user-secrets or environment configuration.");
        }
    }
}
