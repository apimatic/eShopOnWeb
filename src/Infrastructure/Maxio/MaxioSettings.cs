namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are never committed to
/// the repository — they are supplied via environment variables loaded into .NET user-secrets:
/// <list type="bullet">
///   <item><see cref="ApiKey"/> ← <c>MAXIO_API_KEY</c></item>
///   <item><see cref="Subdomain"/> ← <c>MAXIO_SITE_SUBDOMAIN</c></item>
///   <item><see cref="ProductFamilyHandle"/> ← <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c></item>
///   <item><see cref="BaseUrl"/> — optional explicit override; when set it is used verbatim as the
///   API base address instead of deriving one from <see cref="Subdomain"/>.</item>
/// </list>
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain; the API base becomes <c>https://{subdomain}.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional explicit base-URL override; when set, used verbatim in place of the derived URL.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The default plan (product handle) to subscribe to when the request specifies none.</summary>
    public string DefaultProductHandle { get; set; } = "eshop-pro";

    /// <summary>
    /// Payment collection method for new subscriptions. Because the configured plans require no
    /// payment method, paid plans are billed by invoice rather than an auto-charge that needs a card.
    /// Valid values depend on the Maxio site's billing architecture: <c>remittance</c> (Relationship
    /// Invoicing) or <c>invoice</c> (legacy Statements); <c>automatic</c> auto-charges a card,
    /// <c>prepaid</c> is prepaid. Empty leaves it unset (provider default = automatic).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Optional invoice net-terms window (e.g. "0", "30") for remittance/invoice billing.</summary>
    public string? NetTerms { get; set; }

    /// <summary>Total per-operation time budget for provider calls (bounds the whole call, retries included).</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
