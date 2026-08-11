namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are never hard-coded; they
/// are supplied through configuration / user-secrets so the same build can run against any account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>e.g. "sandbox". Reserved for environment selection (this build targets sandbox).</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code the merchant charges in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call (including the OAuth token request) instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
