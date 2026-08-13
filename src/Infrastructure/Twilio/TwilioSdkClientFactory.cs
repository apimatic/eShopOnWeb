using System;
using System.Net.Http;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Builds a configured <see cref="TwilioSdkClient"/> from <see cref="TwilioSettings"/>. Server/base-URL
/// selection in this SDK is per-capability (per server node): the messaging node ("Default") is where
/// <see cref="TwilioSettings.BaseUrl"/> is applied, while the lookup node ("Default4") keeps its own
/// host. The base URL must be set on the node before the client is constructed.
/// </summary>
public static class TwilioSdkClientFactory
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
            // Bound each attempt; the whole call is additionally bounded by the CancellationToken.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Messaging node only. Lookups (Default4) intentionally keep their default host.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
        }

        return new TwilioSdkClient(httpClient, options);
    }
}
