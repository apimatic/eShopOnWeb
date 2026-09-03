namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values are supplied via
/// environment/user-secrets; none are hard-coded so the same build runs against a different account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (the only environment the SDK models). Kept for configurability/logging.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit base URL. When set, it is used verbatim as the API base for every PayPal
    /// call — including the OAuth token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
