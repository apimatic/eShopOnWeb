namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound options for the PayPal payment gateway. Credentials are read from configuration
/// (never hard-coded); the currency is owned by the gateway so callers only supply amounts.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional base-URL override; when set it governs ALL calls including the token request.</summary>
    public string? BaseUrl { get; set; }
}
