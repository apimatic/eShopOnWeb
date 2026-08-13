using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public static class TwilioMessagingServiceExtensions
{
    /// <summary>
    /// Registers the Twilio-backed <see cref="ISmsProvider"/>. Bind <see cref="TwilioSettings"/> from the
    /// <c>Twilio:</c> section before calling this (done in the host). The provider is only constructed when a
    /// messaging capability is actually used, and it refuses to build with incomplete credentials — naming
    /// the missing keys, never echoing their values.
    /// </summary>
    public static IServiceCollection AddTwilioMessaging(this IServiceCollection services)
    {
        // One long-lived client for the app's lifetime. A SocketsHttpHandler with a pooled-connection
        // lifetime keeps DNS fresh behind the singleton; HttpClient.Timeout is a per-attempt backstop.
        services.AddSingleton<TwilioSdkClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            GuardConfigured(settings);

            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                // MaxRetries at its floor keeps a create-message (a non-idempotent POST) from being resent
                // more than once on a transport fault, while still allowing one retry on transient reads.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            // Twilio:BaseUrl, when set, overrides ONLY the messaging host — the Lookup host is left at its
            // default, since the two are independent properties on ServerOptions.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            return new TwilioSdkClient(httpClient, options);
        });

        services.AddScoped<ISmsProvider, TwilioSmsProvider>();
        return services;
    }

    private static void GuardConfigured(TwilioSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.AccountSid)) missing.Add("Twilio:AccountSid");
        if (string.IsNullOrWhiteSpace(settings.AuthToken)) missing.Add("Twilio:AuthToken");
        if (string.IsNullOrWhiteSpace(settings.FromNumber)) missing.Add("Twilio:FromNumber");
        if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid)) missing.Add("Twilio:MessagingServiceSid");

        if (missing.Count > 0)
        {
            throw new SmsProviderException(
                $"Twilio messaging is not configured. Missing: {string.Join(", ", missing)}. " +
                "Set these via environment variables or user-secrets before using the messaging endpoints.");
        }
    }
}
