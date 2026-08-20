namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. No value is hard-coded;
/// the same build runs against a different PayPal account purely by changing configuration.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id of the merchant's sandbox/live app.</summary>
    public string? ClientId { get; set; }

    /// <summary>REST client secret of the merchant's sandbox/live app.</summary>
    public string? ClientSecret { get; set; }

    /// <summary><c>sandbox</c> or <c>production</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for every amount (e.g. USD).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call — including the OAuth/token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsProduction =>
        string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase);
}
