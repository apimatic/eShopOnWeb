using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the <c>Maxio</c>
/// configuration section (plan §2.3 / §5). Only <see cref="ApiKey"/> is sensitive and it must come
/// from .NET user-secrets or an environment variable — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";
    private const string EuropeanRegion = "EU";

    /// <summary>The Maxio API key. Sensitive.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit base URL is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-centre region, <c>US</c> (default) or <c>EU</c>. This is a different axis from
    /// the deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string? Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit outbound base URL. When set it wins over the subdomain-derived host, so the
    /// same build can be pointed at production, a dev/sandbox tenant, or a local mock server purely
    /// through configuration (plan §2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The stable handle of the product family that holds the plans and the metered component.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// The product family identifier. Used only when no <see cref="ProductFamilyHandle"/> is
    /// configured — handles are stable, provider-assigned identifiers are not (plan §1.3).
    /// </summary>
    public int ProductFamilyId { get; set; }

    /// <summary>The handle of the plan the storefront offers by default.</summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>The default plan's provider-assigned identifier, recorded for operator reference.</summary>
    public int DefaultProductId { get; set; }

    /// <summary>The handle of the alternate plan used for upgrade / downgrade.</summary>
    public string? AlternateProductHandle { get; set; }

    /// <summary>The alternate plan's provider-assigned identifier, recorded for operator reference.</summary>
    public int AlternateProductId { get; set; }

    /// <summary>The handle of the metered component usage is reported against (UC2).</summary>
    public string? MeteredComponentHandle { get; set; }

    /// <summary>The metered component's provider-assigned identifier, recorded for operator reference.</summary>
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// How the provider should collect payment for new subscriptions — for example <c>remittance</c>
    /// to invoice the customer, which is what lets a plan with no payment method on file be
    /// subscribed to without card capture (plan §1.3). Left empty, the provider's own default applies.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>How long a single billing operation may take, retries included. Defaults to 30 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a failed idempotent (read) request is retried. Writes are never retried, so a
    /// transport failure can never double-bill. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Whether the configured region is the European data centre.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), EuropeanRegion, StringComparison.OrdinalIgnoreCase);

    /// <summary>The operation timeout as a <see cref="TimeSpan"/>, clamped to a sane range.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300));

    /// <summary>The retry count, clamped to a sane range.</summary>
    public int RetryCount => Math.Clamp(MaxRetries, 0, 10);

    /// <summary>
    /// The single place the outbound target is decided: an explicit <see cref="BaseUrl"/> is used
    /// verbatim, otherwise the host is derived from <see cref="Subdomain"/> and the region.
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain is configured, or the explicit value is not a
    /// valid absolute http(s) URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl.Trim();

            if (!Uri.TryCreate(configured, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new BillingConfigurationException(
                    $"'{SectionName}:{nameof(BaseUrl)}' must be an absolute http or https URL.");
            }

            return configured.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"The billing integration needs either '{SectionName}:{nameof(BaseUrl)}' or '{SectionName}:{nameof(Subdomain)}' to be configured.");
        }

        return string.Format(CultureInfo.InvariantCulture,
            IsEuropeanRegion ? EuHostTemplate : UsHostTemplate, Subdomain.Trim());
    }

    /// <summary>
    /// Resolves the outbound base URL without throwing, for composition-root code that must not fail
    /// when the integration is not configured on this host.
    /// </summary>
    public bool TryResolveBaseUrl(out string? baseUrl)
    {
        try
        {
            baseUrl = ResolveBaseUrl();
            return true;
        }
        catch (BillingConfigurationException)
        {
            baseUrl = null;
            return false;
        }
    }
}
