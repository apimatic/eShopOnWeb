namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied
/// via configuration / user-secrets (never hard-coded), so the same build runs against any account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id of the sandbox/live business account.</summary>
    public string? ClientId { get; set; }

    /// <summary>REST client secret of the business account.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment: "sandbox" or "live".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency all amounts are denominated in (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional API base-URL override. When set it is used verbatim for EVERY PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
