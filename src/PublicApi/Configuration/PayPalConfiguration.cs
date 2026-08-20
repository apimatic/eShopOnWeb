using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi;

internal static class PayPalConfiguration
{
    public static void ApplyEnvironmentVariables(ConfigurationManager configuration)
    {
        var overlay = new Dictionary<string, string?>();
        Copy(configuration, overlay, "PAYPAL_CLIENT_ID", "PayPal:ClientId");
        Copy(configuration, overlay, "PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret");
        Copy(configuration, overlay, "PAYPAL_ENVIRONMENT", "PayPal:Environment");
        Copy(configuration, overlay, "PAYPAL_CURRENCY", "PayPal:Currency");
        Copy(configuration, overlay, "PAYPAL_BASE_URL", "PayPal:BaseUrl");

        if (overlay.Count > 0)
        {
            configuration.AddInMemoryCollection(overlay);
        }
    }

    private static void Copy(IConfiguration configuration, Dictionary<string, string?> overlay, string sourceKey, string targetKey)
    {
        var value = configuration[sourceKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            overlay[targetKey] = value;
        }
    }
}
