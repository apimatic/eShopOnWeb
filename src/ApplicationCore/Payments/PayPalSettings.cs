namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied through
/// configuration / user-secrets (never hard-coded), so the same build runs against any PayPal
/// account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment: <c>sandbox</c> or <c>live</c> (from <c>PAYPAL_ENVIRONMENT</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Currency all money moves in (from <c>PAYPAL_CURRENCY</c>), e.g. USD.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit base URL. When set, it is used verbatim for every PayPal call — including
    /// the token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The effective API base URL: the explicit <see cref="BaseUrl"/> override if present, otherwise
    /// the standard sandbox or live host for the configured environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
