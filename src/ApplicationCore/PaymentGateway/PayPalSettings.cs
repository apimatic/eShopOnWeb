namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// PayPal integration settings, bound from the "PayPal" configuration section. Values are never
/// hard-coded — they are supplied via configuration/user-secrets so the same build can run against
/// a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live" — selects the default API host when <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code (e.g. "USD") that all amounts are expressed in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call
    /// (including the token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address: the explicit override if present, else per environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var isLive = Environment?.Trim().ToLowerInvariant() is "live" or "production";
        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
