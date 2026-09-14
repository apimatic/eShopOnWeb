namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section. Credentials are supplied via
/// configuration (user-secrets / environment) and are never hard-coded, so the same build can run
/// against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic-auth username). Bound from <c>Maxio:ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain used to derive the API base address. Bound from <c>Maxio:Subdomain</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as plans. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base-address override. When set, it is used as-is instead of deriving the
    /// address from <see cref="Subdomain"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Display currency for plan/subscription prices. The Maxio product model carries a price amount but
    /// no currency, so this fills that gap for presentation. Optional; defaults to USD.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Payment-collection method for new subscriptions. For plans that do not require a card, this keeps
    /// subscribe working without card capture. Optional; defaults to "remittance" (Relationship Invoicing).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}
