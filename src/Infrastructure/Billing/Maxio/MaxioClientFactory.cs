using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>Builds the long-lived Maxio SDK client from validated <see cref="MaxioSettings"/>.</summary>
public static class MaxioClientFactory
{
    // Maxio's Basic scheme: the API key is the username and the password is the literal "x".
    private const string BasicAuthPassword = "x";

    public static MaxioAdvancedBillingClient Create(HttpClient httpClient, MaxioSettings settings, ILoggerFactory loggerFactory)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = BasicAuthPassword
            },
            // Defaults retry GET/HEAD/PUT/OPTIONS only, so neither create (POST) is ever resent by the SDK.
            // Keep at most 3 attempts so a read stays inside the per-request budget.
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = settings.AttemptTimeout
            },
            // Assign the factory explicitly so the SDK's environment-variable logger (which can force
            // request-body logging) is never armed. Bodies carry customer e-mail and names: keep them out.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        options.Environment = TryParseEnvironment(settings.Environment, out var environment)
            ? environment
            : ServerEnvironment.Default();

        // The environment is captured at construction, so configure both regions' server options:
        // whichever is selected reads its own values.
        var production = options.Server.Production;
        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            production.Us.Site = settings.Subdomain!.Trim();
            production.Eu.Site = settings.Subdomain!.Trim();
        }
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Override used verbatim (a trailing slash would double up with the operation path).
            var baseUrl = settings.BaseUrl!.Trim().TrimEnd('/');
            production.Us.BaseUrl = baseUrl;
            production.Eu.BaseUrl = baseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    public static bool TryParseEnvironment(string? value, [NotNullWhen(true)] out ServerEnvironment? environment)
    {
        environment = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        return ServerEnvironment.TryGetKnownValue(value!.Trim().ToUpperInvariant(), out environment);
    }
}
