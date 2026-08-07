namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied only via configuration
/// (env vars / user-secrets) and are never hard-coded, so the same build can target a different
/// PayPal app. Only the sandbox environment is supported for this integration.
/// </summary>
public class PayPalSettings
{
    public const string ConfigSection = "PayPal";

    /// <summary>The PayPal REST app client id (<c>PayPal:ClientId</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>The PayPal REST app client secret (<c>PayPal:ClientSecret</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The PayPal environment (<c>PayPal:Environment</c>); always <c>sandbox</c> here.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional base URL override (<c>PayPal:BaseUrl</c>). When set it is used verbatim as the API
    /// base address; otherwise the address is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The sandbox base URL declared by the PayPal OpenAPI specs
    /// (servers[].url = https://api-m.sandbox.paypal.com).
    /// </summary>
    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> override when provided,
    /// otherwise the sandbox base URL from the specs. Returned without a trailing slash.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return SandboxBaseUrl;
    }
}
