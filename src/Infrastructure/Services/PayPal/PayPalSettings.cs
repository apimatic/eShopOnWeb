namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied from configuration
/// (user-secrets / environment) - never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Always "sandbox" for this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolves the API base address: the explicit override when provided, else derived from
    /// the environment (sandbox by default).</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;
    }
}
