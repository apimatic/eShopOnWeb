namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied through
/// .NET user-secrets or environment variables; none are hard-coded or committed.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "production". Selects the PayPal API host unless <see cref="BaseUrl"/> is set.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for every payment operation (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override for the PayPal API base address. When set it is used verbatim for
    /// every PayPal call, including the OAuth token request, instead of deriving the host
    /// from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
