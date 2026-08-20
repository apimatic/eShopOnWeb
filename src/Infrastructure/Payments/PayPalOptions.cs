namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        return IsLiveEnvironment(Environment)
            ? "https://api-m.paypal.com/"
            : "https://api-m.sandbox.paypal.com/";
    }

    private static bool IsLiveEnvironment(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return false;
        }

        return environment.Equals("live", System.StringComparison.OrdinalIgnoreCase) ||
               environment.Equals("production", System.StringComparison.OrdinalIgnoreCase);
    }
}
