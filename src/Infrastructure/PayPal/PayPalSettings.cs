namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied from configuration
/// (environment / user-secrets) and are never hard-coded, so the same build runs against a
/// different PayPal account by changing configuration alone.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>sandbox</c> or <c>live</c> (from <c>PAYPAL_ENVIRONMENT</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency the merchant transacts in (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call —
    /// including the OAuth token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API base address to use, honouring <see cref="BaseUrl"/> when set.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com",
        };
    }
}
