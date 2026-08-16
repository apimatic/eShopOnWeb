namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are never hard-coded — they are
/// supplied via configuration/user-secrets so the same build runs against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>sandbox</c> or <c>live</c>. Used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency the whole integration transacts in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call — including the
    /// token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base address, honouring the <see cref="BaseUrl"/> override.</summary>
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
