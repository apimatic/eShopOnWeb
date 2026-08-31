using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalSettings
{
    public const string SectionName = "PayPal";

    // Base URL for the PayPal sandbox environment, taken from the `servers` entry of the
    // PayPal OpenAPI specifications in api-specs/paypal.
    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;
    }
}
