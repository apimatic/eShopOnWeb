using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioBaseUrl
{
    public const string DefaultPlaceholder = "https://invalid.invalid/";

    public static Uri Resolve(MaxioOptions options, string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return Normalize(options.BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return new Uri(DefaultPlaceholder);
        }

        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return new Uri($"https://{options.Subdomain.Trim()}.{host}/");
    }

    private static Uri Normalize(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return new Uri(trimmed + "/", UriKind.Absolute);
    }
}
