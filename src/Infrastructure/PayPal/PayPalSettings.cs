namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied by the
/// environment/secret store and never live in the repository. <see cref="BaseUrl"/> is an
/// optional override: when set it is used verbatim as the API base address for every PayPal call.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal environment name (e.g. "sandbox").</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code all payments are charged in (e.g. "USD").</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional base-URL override; when set, used verbatim for every PayPal call.</summary>
    public string? BaseUrl { get; set; }
}
