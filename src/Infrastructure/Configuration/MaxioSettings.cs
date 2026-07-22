using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the "Maxio"
/// section (mirrors how <see cref="CatalogSettings"/> is bound). Only <see cref="ApiKey"/> is
/// sensitive and it arrives through .NET user-secrets; the handles and ids are environment
/// metadata.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Never committed — supplied through user-secrets.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, used to derive the host when no explicit override is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The Maxio data-centre region ("US" or "EU"). This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Explicit outbound base URL. When set it wins verbatim over the subdomain-derived host,
    /// so the same build can be pointed at production, a dev/sandbox tenant, or a local mock
    /// purely through configuration.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public int ProductFamilyId { get; set; }

    /// <summary>The plan offered by default on the storefront.</summary>
    public string? DefaultProductHandle { get; set; }

    public int DefaultProductId { get; set; }

    /// <summary>The second plan, so a customer can upgrade or downgrade between the two.</summary>
    public string? AlternateProductHandle { get; set; }

    public int AlternateProductId { get; set; }

    /// <summary>The metered component pay-as-you-go usage accrues against.</summary>
    public string? MeteredComponentHandle { get; set; }

    public int MeteredComponentId { get; set; }

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is honoured verbatim,
    /// otherwise the host is derived from <see cref="Subdomain"/> and the region.
    /// </summary>
    /// <exception cref="BillingConfigurationException">
    /// Neither an explicit base URL nor a subdomain was configured.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Neither '{CONFIG_SECTION}:BaseUrl' nor '{CONFIG_SECTION}:Subdomain' is configured; the billing client has no server to target.");
        }

        var template = IsEuropeanRegion() ? EuHostTemplate : UsHostTemplate;

        return string.Format(template, Subdomain.Trim());
    }

    private bool IsEuropeanRegion() =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}
