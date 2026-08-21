namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied at runtime (env vars → user-secrets); none are hard-coded so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal environment name, e.g. "sandbox".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim for every PayPal call
    /// (including the token request); when empty, the environment's default host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
