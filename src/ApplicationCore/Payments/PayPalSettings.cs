namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values are supplied via
/// environment / user-secrets and are never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"Sandbox" or "Production". Selects the SDK environment when BaseUrl is not overridden.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for all amounts (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it is used for every PayPal call (including the
    /// token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolvedCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();
}
