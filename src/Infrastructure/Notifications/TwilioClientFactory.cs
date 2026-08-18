using System;
using System.Net.Http;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Builds the long-lived Twilio client from settings and a caller-owned <see cref="HttpClient"/>.
/// This keeps every SDK construction detail — auth, the messaging-only base-URL override, and the
/// resilience knobs — inside the Infrastructure layer; the host only supplies the pieces it owns
/// (the pooled <see cref="HttpClient"/>) and the validated settings.
/// </summary>
public static class TwilioClientFactory
{
    public static TwilioSdkClient Create(TwilioSettings settings, HttpClient httpClient)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            },
            // A send is a non-idempotent POST with no provider idempotency key; keep transport
            // re-send exposure to the enforced minimum (one retry) while still recovering reads.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(15)
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Override the MESSAGING host only (server node "Default"); the Lookup host
            // (node "Default4") is deliberately left at its provider default.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
        }

        return new TwilioSdkClient(httpClient, options);
    }
}
