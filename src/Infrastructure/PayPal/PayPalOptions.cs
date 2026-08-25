using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Bound from the "PayPal" configuration section. Values come from user-secrets/environment, never hard-coded.</summary>
public class PayPalOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, used verbatim as the base address for every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl!.TrimEnd('/');

        return Environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
               Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
