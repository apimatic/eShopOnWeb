namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Configuration for the payment gateway, bound from the "PayPal" configuration section.
/// Values are never hard-coded: they come from configuration (user secrets / environment variables).
/// </summary>
public class PaymentOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional verbatim override of the API base address (including the OAuth token endpoint).
    /// When set, it is used for every gateway call instead of a URL derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
