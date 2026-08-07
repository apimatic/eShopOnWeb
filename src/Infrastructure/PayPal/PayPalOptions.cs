using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal</c> configuration section. Values are
/// supplied through user-secrets / environment (never committed to the repo).
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>PayPal REST app client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST app client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment. For this integration always <c>sandbox</c>.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base address to call PayPal at.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. via user-secrets from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
        }
    }
}
