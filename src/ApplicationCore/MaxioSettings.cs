using System;

namespace Microsoft.eShopWeb;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    public string? ProductFamilyHandle { get; set; }
    public int ProductFamilyId { get; set; }

    public string? DefaultProductHandle { get; set; }
    public int DefaultProductId { get; set; }

    public string? AlternateProductHandle { get; set; }
    public int AlternateProductId { get; set; }

    public string? MeteredComponentHandle { get; set; }
    public int MeteredComponentId { get; set; }

    public string ResolveBaseUrl()
    {
        // Explicit BaseUrl override wins; otherwise derive from Subdomain + Environment
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        // Default derivation from subdomain and environment (region)
        // Environment: "US" -> chargify.com; "EU" -> ebilling.maxio.com
        var baseHost = Environment?.Equals("EU", StringComparison.OrdinalIgnoreCase) == true
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return $"https://{baseHost}";
    }
}
