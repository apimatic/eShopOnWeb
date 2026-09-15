namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section. Values are
/// supplied via .NET user-secrets / environment and are never hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production". Selects the default API host when BaseUrl is unset.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO currency code used for all amounts (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
