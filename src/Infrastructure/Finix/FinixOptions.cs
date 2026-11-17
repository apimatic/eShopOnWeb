namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://finix.live-payments-api.com"
            : "https://finix.sandbox-payments-api.com";
    }
}
