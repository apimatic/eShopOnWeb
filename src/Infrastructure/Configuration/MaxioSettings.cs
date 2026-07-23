using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section exactly like <c>CatalogSettings</c> (plan.md §2.3).
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is sensitive and it must come from .NET user-secrets or an environment
/// variable — never from a file in the repository.
/// </remarks>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Base-URL template for the US data-center region.</summary>
    public const string UsBaseUrlTemplate = "https://{site}.chargify.com";

    /// <summary>Base-URL template for the EU data-center region.</summary>
    public const string EuBaseUrlTemplate = "https://{site}.ebilling.maxio.com";

    /// <summary>The site API key. Secret — user-secrets or environment variable only.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the host when no explicit base URL is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio <em>data-center region</em>, <c>US</c> or <c>EU</c>. This is a separate axis from the
    /// deployment target (production / dev / mock), which <see cref="BaseUrl"/> controls (plan.md §2.3).
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit outbound base URL. When set it wins over the <see cref="Subdomain"/>-derived
    /// host verbatim, so the identical build can be pointed at production, a dev/sandbox tenant, or a
    /// local mock server purely through configuration (plan.md §2.3 — a hard requirement).
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Last-known numeric id of the product family. Handles are durable and ids are not, so this is only
    /// a fallback for when the handle cannot be resolved (plan.md §1.3).
    /// </summary>
    public int? ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;

    public int? DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;

    public int? AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;

    public int? MeteredComponentId { get; set; }

    /// <summary>True when the configured region is the EU data center.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> wins verbatim, otherwise the
    /// host is derived from <see cref="Subdomain"/> and the region (plan.md §2.3/§4.3).
    /// </summary>
    /// <remarks>
    /// This is the single place retargeting happens, so pointing the build at another server is a
    /// configuration change and never a code change.
    /// </remarks>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"Maxio is not configured: set either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        var template = IsEuropeanRegion ? EuBaseUrlTemplate : UsBaseUrlTemplate;
        return template.Replace("{site}", Subdomain.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Fails fast with an actionable message when a required setting is missing, rather than letting the
    /// integration make an unauthenticated or misdirected call.
    /// </summary>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{SectionName}:ApiKey");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            missing.Add($"{SectionName}:Subdomain (or {SectionName}:BaseUrl)");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{SectionName}:ProductFamilyHandle");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            missing.Add($"{SectionName}:MeteredComponentHandle");
        }

        if (missing.Count > 0)
        {
            throw new BillingConfigurationException(
                "The Maxio integration is missing required configuration: " + string.Join(", ", missing) +
                ". Set these in .NET user-secrets (see plan.md §5); never commit the API key.");
        }
    }
}
