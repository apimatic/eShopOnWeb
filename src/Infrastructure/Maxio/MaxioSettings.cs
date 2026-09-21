namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed configuration for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Values are supplied out-of-band (user-secrets / environment)
/// and never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Bound from <c>Maxio:ApiKey</c>. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain. Bound from <c>Maxio:Subdomain</c>. Required.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family whose plans are offered. Bound from
    /// <c>Maxio:ProductFamilyHandle</c>. Required.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. Bound from <c>Maxio:BaseUrl</c>. When set it is used
    /// verbatim as the base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
