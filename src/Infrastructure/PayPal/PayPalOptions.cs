namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal</c> configuration section.
/// Values are supplied via configuration / user-secrets and are never hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>OAuth2 client id (config key <c>PayPal:ClientId</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth2 client secret (config key <c>PayPal:ClientSecret</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment (config key <c>PayPal:Environment</c>). Always <c>sandbox</c> for this task.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional explicit API base URL (config key <c>PayPal:BaseUrl</c>). When set it is used verbatim;
    /// otherwise the base URL is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. A configured <see cref="BaseUrl"/> wins; otherwise the sandbox
    /// server URL declared by the PayPal OpenAPI specifications is used.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        // The PayPal OpenAPI specs (api-specs/paypal/**) declare a single server for the sandbox
        // environment: https://api-m.sandbox.paypal.com. This task only targets sandbox.
        return "https://api-m.sandbox.paypal.com";
    }
}
