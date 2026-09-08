namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values come from user-secrets / environment
/// variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY)
/// and are never committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>
    /// The Maxio API key. Sent as the Basic-auth username.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Maxio site subdomain (sandbox or production site).
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. The plan subscribed to when the caller does not name one.
    /// When unset, the first plan in the family is the default target.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Subdomain);
}
