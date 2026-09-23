namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. Values come from
/// user-secrets (dev) or environment variables (<c>PayPal__*</c>) — never from a file in the repo.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id of the sandbox/live business account (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment name (from <c>PAYPAL_ENVIRONMENT</c>); "sandbox" is the SDK's only member.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency for order amounts (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional base-URL override used verbatim for every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        string.IsNullOrWhiteSpace(Environment) ||
        Environment.Trim().Equals("sandbox", System.StringComparison.OrdinalIgnoreCase);
}
