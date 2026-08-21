namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal settings bound from the "PayPal" configuration section. Values are never
/// hard-coded — client id/secret come from user-secrets/environment; the same build runs against a
/// different PayPal account by changing configuration only.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal environment name (e.g. "sandbox").</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth/token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
