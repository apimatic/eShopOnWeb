namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Strongly-typed settings bound from the "Maxio" configuration section.
/// Values are supplied via .NET user-secrets / environment and must never be
/// hard-coded into the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name these settings bind from.</summary>
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (site API key). From MAXIO_API_KEY.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain. From MAXIO_SITE_SUBDOMAIN.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans.
    /// From MAXIO_DEFAULT_PRODUCT_FAMILY.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL override. When set, it is used verbatim as the
    /// API base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the minimum settings required to talk to Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
