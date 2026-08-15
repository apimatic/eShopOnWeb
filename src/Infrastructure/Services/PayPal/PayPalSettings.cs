namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal:" section. Values are never
/// hard-coded — they come from configuration/user-secrets/environment.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "production". Only sandbox is targeted here.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for EVERY PayPal call —
    /// including the OAuth token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
