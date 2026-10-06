namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration
/// (user-secrets / environment variables) and are never committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (sent as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. "acme-sandbox" -> https://{subdomain}.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product-family handle that owns the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it is used instead of deriving
    /// the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
