namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal integration settings, bound from the "PayPal:" configuration section. Values are supplied via
/// user-secrets / environment and are never hard-coded, so the same build runs against a different account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Selects the PayPal environment when <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim for every PayPal call — including the
    /// OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
