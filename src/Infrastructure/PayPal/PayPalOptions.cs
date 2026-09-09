namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings bound from the <c>PayPal:</c> configuration section. Values are
/// never hard-coded and never written into the repository — they come from configuration
/// (user-secrets / environment), so the same build runs against any PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id (from <c>PayPal:ClientId</c> ← env <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>REST client secret (from <c>PayPal:ClientSecret</c> ← env <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment (from <c>PayPal:Environment</c> ← env <c>PAYPAL_ENVIRONMENT</c>), e.g. <c>sandbox</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Three-letter currency code for amounts (from <c>PayPal:Currency</c> ← env <c>PAYPAL_CURRENCY</c>).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override (<c>PayPal:BaseUrl</c>). When set, it is used verbatim as the API base
    /// address for every PayPal call — including the OAuth token request — instead of one derived from
    /// the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
