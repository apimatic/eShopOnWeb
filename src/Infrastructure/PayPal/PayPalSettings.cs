namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values are supplied by
/// configuration (env vars / user-secrets) and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>e.g. "Sandbox". Only Sandbox is supported by this SDK build unless BaseUrl is overridden.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 settlement currency, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
