namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal integration settings, bound from the "PayPal" configuration section. Values are supplied
/// via environment / user-secrets (never committed): ClientId, ClientSecret, Environment, Currency,
/// and an optional BaseUrl override.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal environment (e.g. "sandbox"). Informational; the SDK targets sandbox and the
    /// base host is chosen by <see cref="BaseUrl"/> when set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call,
    /// including the OAuth token request. When empty the SDK's sandbox default host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
