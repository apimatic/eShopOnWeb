using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio:</c> configuration section. Secret values are supplied out-of-band (user-secrets /
/// environment) and are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Chargify API key (from <c>MAXIO_API_KEY</c>). Used as the Basic-auth username.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from <c>MAXIO_SITE_SUBDOMAIN</c>). Fills <c>{site}</c> in
    /// <c>https://{site}.chargify.com</c>. Optional only when <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are exposed as subscription plans
    /// (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>). Handles are stable; numeric IDs are not.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base-URL override. When set it is used exactly as given
    /// (instead of deriving one from <see cref="Subdomain"/>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method for new subscriptions. Defaults to <c>invoice</c> so subscriptions
    /// activate without capturing a card (the seeded plans require no payment method). On a
    /// Relationship Invoicing site use <c>remittance</c>; use <c>automatic</c> only when a payment
    /// profile is captured. Set empty to leave the server default.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "invoice";

    /// <summary>Whole-call budget (per SDK call) enforced via a linked CancellationToken.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Per-attempt SDK timeout.</summary>
    public int PerAttemptTimeoutSeconds { get; set; } = 15;
}
