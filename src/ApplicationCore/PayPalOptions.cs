namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the <c>PayPal</c> configuration section. Credential values are
/// read from environment variables / user-secrets and are never hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>e.g. <c>sandbox</c> or <c>live</c>.</summary>
    public string Environment { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address for every
    /// PayPal call — including the credential/token request — instead of deriving one
    /// from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}