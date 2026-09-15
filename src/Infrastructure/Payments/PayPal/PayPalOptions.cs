using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

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
            return BaseUrl.TrimEnd('/');
        }

        if (string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    public static void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        Apply("PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Apply("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Apply("PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Apply("PAYPAL_CURRENCY", "PayPal:Currency");

        void Apply(string environmentVariable, string configurationKey)
        {
            var value = System.Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                configuration[configurationKey] = value;
            }
        }
    }
}
