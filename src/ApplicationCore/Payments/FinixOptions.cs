using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://finix.live-payments-api.com"
            : "https://finix.sandbox-payments-api.com";
    }

    public string ResolveCurrency() =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.ToUpperInvariant();
}
