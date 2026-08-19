using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of deriving a URL from Subdomain.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional Maxio hosting region from MAXIO_ENVIRONMENT (US or EU).
    /// Used only when BaseUrl is not set.
    /// </summary>
    public string Environment { get; set; } = "US";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim().TrimEnd('/');
            return new Uri(trimmed + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        var host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain.Trim()}.ebilling.maxio.com"
            : $"{Subdomain.Trim()}.chargify.com";
        return new Uri($"https://{host}/", UriKind.Absolute);
    }
}
