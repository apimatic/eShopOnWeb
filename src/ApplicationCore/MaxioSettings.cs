namespace Microsoft.eShopWeb;

/// <summary>
/// Binds to the "Maxio" configuration section. Populate via user-secrets in Development
/// and via Maxio__* environment variables (or another config provider) elsewhere - never
/// hard-code these values, since the same build must be able to target a different Maxio
/// site and product catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio Advanced Billing API key, used as the HTTP Basic Auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "cp-exp-2" for https://cp-exp-2.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving a URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
