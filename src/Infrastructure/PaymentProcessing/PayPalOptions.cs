namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Bound from the "PayPal" configuration section. Values come from user-secrets (local dev) or
/// environment variables — never hard-coded, so the same build can run against a different
/// PayPal account than the one used during development.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, used verbatim as the API base address for every PayPal call, including the OAuth token request.</summary>
    public string? BaseUrl { get; set; }
}
