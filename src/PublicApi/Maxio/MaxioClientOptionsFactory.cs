using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Builds the SDK client options from validated <see cref="MaxioOptions"/>. Kept separate from DI so tests can
/// build the exact production options over a stub <see cref="System.Net.Http.HttpClient"/>.
/// </summary>
public static class MaxioClientOptionsFactory
{
    /// <summary>Per-attempt timeout; the SDK retries GETs on its expiry, never POSTs.</summary>
    public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Retries for idempotent verbs only (HttpMethodsToRetry stays at its default: no POST).</summary>
    public const int MaxRetries = 2;

    public static MaxioAdvancedBillingClientOptions Create(MaxioOptions settings, ILoggerFactory loggerFactory, TimeProvider? timeProvider = null)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            // Sandbox vs live is a property of the site + key, not of the environment; the SDK declares Us / Eu.
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                // The documented password for Maxio API-key auth is the literal "x"; it is not a secret.
                Password = "x",
            },
            Retry = RetryOptions.Default() with
            {
                Timeout = PerAttemptTimeout,
                MaxRetries = MaxRetries,
            },
            // LoggerFactory is assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG variable can never turn
            // on body logging; customer e-mail and names travel in JSON bodies.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
            TimeProvider = timeProvider ?? TimeProvider.System,
        };

        // Only the selected environment's (Us) server options are read.
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Server.Production.Us.Site = settings.Subdomain!;
        }

        return options;
    }
}
