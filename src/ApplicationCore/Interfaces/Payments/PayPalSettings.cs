namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Strongly-typed view of the <c>PayPal:</c> configuration section. Values are supplied via
/// configuration / user-secrets / environment variables and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>PayPal environment name (e.g. "sandbox").</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code all amounts are expressed in (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional verbatim API base URL. When set it is used unchanged for every PayPal call
    /// (including the OAuth2 token request), overriding the environment-derived host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
