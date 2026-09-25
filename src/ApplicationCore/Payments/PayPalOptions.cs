namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are supplied per deployment
/// (user-secrets / environment); none are hard-coded, so the same build runs against a different PayPal account.
/// </summary>
public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST client id (from <c>PayPal:ClientId</c> ← env <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>PayPal REST client secret (from <c>PayPal:ClientSecret</c> ← env <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment (from <c>PayPal:Environment</c> ← env <c>PAYPAL_ENVIRONMENT</c>); "sandbox" here.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency for all amounts (from <c>PayPal:Currency</c> ← env <c>PAYPAL_CURRENCY</c>).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional API base-URL override (from <c>PayPal:BaseUrl</c>). When set, it is used verbatim as the base
    /// address for every PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the caller supplied an explicit base URL to use verbatim.</summary>
    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}
