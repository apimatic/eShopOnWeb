namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via
/// environment variables / user-secrets — never hard-coded or committed.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address for every
    /// PayPal call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
