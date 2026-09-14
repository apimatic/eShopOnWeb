using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.MaxioBilling.Configuration;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioBilling.Services;

/// <summary>
/// Owns the single long-lived <see cref="MaxioAdvancedBillingClient"/>, or the reason there isn't one.
/// <para>
/// The SDK's own <c>AddMaxioAdvancedBillingClient</c> extension is deliberately not used: it registers a
/// singleton over the shared, unnamed <see cref="IHttpClientFactory"/> client, so a timeout or handler set
/// for Maxio would change every other unnamed consumer in the process, and its captured handler never
/// rotates. This builds the client over a named client instead — see
/// <see cref="MaxioBillingServiceCollectionExtensions"/>.
/// </para>
/// </summary>
internal sealed class MaxioClientAccessor
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client this integration owns.</summary>
    public const string HttpClientName = "maxio-advanced-billing";

    public MaxioClientAccessor(
        IHttpClientFactory httpClientFactory,
        IOptions<MaxioBillingOptions> options,
        ILogger<MaxioClientAccessor> logger)
    {
        var settings = options.Value;
        ConfigurationProblems = settings.ConfigurationProblems();

        if (ConfigurationProblems.Count > 0)
        {
            // Not fatal: the rest of eShopOnWeb must still start without Maxio credentials.
            // The subscription endpoints report the misconfiguration instead.
            logger.LogWarning(
                "Maxio billing is not configured; subscription endpoints will be unavailable. {Problems}",
                string.Join(" ", ConfigurationProblems));
            return;
        }

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = settings.UseEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                // Maxio authenticates with the API key as the username and a literal "x" as the password.
                Username = settings.ApiKey!,
                Password = "x"
            },
            Retry = RetryOptions.Default() with
            {
                // The SDK floor is 1; 0 throws at construction.
                MaxRetries = Math.Max(1, settings.MaxRetries),
                // Per attempt, not per call. The whole-call budget is a linked token in the service.
                Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds))
            }
        };

        // Server options must be set before the client is constructed: the environment is captured once,
        // and only the selected environment's node is ever read.
        var production = clientOptions.Server.Production;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim. The SDK only substitutes a literal "{site}" token, so a URL without one
            // passes through unchanged and the site subdomain is ignored.
            production.Us.BaseUrl = settings.BaseUrl!;
            production.Eu.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            // Left unset this defaults to the literal string "subdomain", which silently targets the
            // wrong host, so it is always assigned explicitly.
            production.Us.Site = settings.Subdomain!;
            production.Eu.Site = settings.Subdomain!;
        }

        Client = new MaxioAdvancedBillingClient(httpClientFactory.CreateClient(HttpClientName), clientOptions);

        logger.LogInformation(
            "Maxio billing configured for region {Region}, product family '{ProductFamily}'{BaseUrlNote}.",
            settings.UseEuRegion ? "EU" : "US",
            settings.ProductFamilyHandle,
            string.IsNullOrWhiteSpace(settings.BaseUrl) ? string.Empty : " using a configured base URL override");
    }

    /// <summary>The client, or null when the integration is not configured.</summary>
    public MaxioAdvancedBillingClient? Client { get; }

    /// <summary>Why there is no client; empty when there is one.</summary>
    public IReadOnlyList<string> ConfigurationProblems { get; }

    public bool IsConfigured => Client is not null;

    /// <exception cref="BillingException">The integration is not configured.</exception>
    public MaxioAdvancedBillingClient Require() =>
        Client ?? throw new BillingException(
            BillingFailureKind.NotConfigured,
            "Subscription billing is not configured on this server.");
}
