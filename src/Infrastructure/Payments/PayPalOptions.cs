using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the PayPal: configuration section. Values arrive via environment variables
/// (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY) or
/// user-secrets; none are hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>sandbox (default) or live.</summary>
    [Required]
    public string Environment { get; set; } = "sandbox";

    [Required]
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
