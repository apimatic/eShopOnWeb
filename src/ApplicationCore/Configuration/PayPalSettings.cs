using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the "PayPal" configuration section. ClientId/ClientSecret come
/// from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables (or user
/// secrets); no credential values are ever stored in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for
    /// every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
