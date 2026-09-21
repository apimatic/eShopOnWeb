namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are supplied
/// by the operator (environment variables → .NET user-secrets) and never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>REST app secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment (from <c>PAYPAL_ENVIRONMENT</c>); the SDK supports <c>sandbox</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency to charge in (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget (seconds) applied to each gateway operation. Defaults to 100.</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;
}
