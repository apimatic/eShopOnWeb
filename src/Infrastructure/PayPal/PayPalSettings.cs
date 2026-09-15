namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values are supplied by
/// configuration/user-secrets (never hard-coded), so the same build can run against a different account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim for every PayPal call — including the
    /// OAuth token request — instead of the default sandbox host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
