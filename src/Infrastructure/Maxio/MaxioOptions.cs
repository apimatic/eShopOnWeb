using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the <c>Maxio:</c> configuration section. Credential values come from
/// environment variables / user secrets — never from files inside the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (e.g. <c>cp-exp-1</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim API base address override (full host incl. scheme, no path).
    /// When set it replaces the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that hosts the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>The plans offered for enrollment, identified by their Maxio product handles.</summary>
    public List<MaxioPlanOption> Plans { get; set; } = new();
}

public class MaxioPlanOption
{
    /// <summary>Maxio product handle (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Shopper-facing display name used when Maxio cannot resolve the product.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether this is the default plan highlighted in the storefront.</summary>
    public bool IsDefault { get; set; }
}