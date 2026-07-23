using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section
/// (§2.3). Only <see cref="ApiKey"/> is sensitive and it must come from user-secrets or an
/// environment variable — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio's US data-center host template.</summary>
    private const string UsHostFormat = "https://{0}.chargify.com";

    /// <summary>Maxio's EU data-center host template.</summary>
    private const string EuHostFormat = "https://{0}.ebilling.maxio.com";

    /// <summary>Site API key. Secret — supplied through user-secrets or the environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "cp-exp-4".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-center region: "US" or "EU". This is a different axis from the deployment
    /// target (prod / dev / mock), which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the subdomain-derived host, so the same
    /// build can be pointed at production, a dev/sandbox tenant, or a local mock server purely
    /// through configuration (§2.3). Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Handle of the plan the storefront offers by default.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the alternate plan, used as the plan-change target (UC3).</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the metered component usage is recorded against (UC2).</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>
    /// How Maxio should collect payment for new subscriptions: "automatic" (charge a stored
    /// payment method) or "remittance" (invoice the customer).
    /// </summary>
    /// <remarks>
    /// Defaults to "remittance" because a site with no payment gateway configured rejects
    /// automatic collection with "No payment method was on file", which would break the subscribe
    /// flow. A gateway-enabled site switches to "automatic" through configuration alone.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Outbound request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is used verbatim, and only
    /// when it is absent is the host derived from <see cref="Subdomain"/> and the region (§2.3).
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain was configured, or the explicit value is not an
    /// absolute HTTP(S) URL. This is a setup problem, so it surfaces as a configuration error the
    /// storefront can report rather than an unhandled crash.
    /// </exception>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var explicitUri) ||
                (explicitUri.Scheme != Uri.UriSchemeHttp && explicitUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new BillingConfigurationException(
                    $"Maxio:BaseUrl must be an absolute http(s) URL, but was '{BaseUrl}'.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio is not configured: set Maxio:BaseUrl to target a specific server, or Maxio:Subdomain to derive the host from the site name.");
        }

        var format = IsEuropeanRegion ? EuHostFormat : UsHostFormat;
        return new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, Subdomain.Trim()));
    }

    private bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}
