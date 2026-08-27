namespace Microsoft.eShopWeb.ApplicationCore;

public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base
    /// address for every PayPal call, including the token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string EffectiveBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl!.TrimEnd('/');
            }

            return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }
    }
}
