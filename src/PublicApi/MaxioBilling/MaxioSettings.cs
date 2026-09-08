using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"{CONFIG_NAME}:ApiKey");
        if (string.IsNullOrWhiteSpace(Subdomain)) missing.Add($"{CONFIG_NAME}:Subdomain");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"{CONFIG_NAME}:ProductFamilyHandle");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Missing required configuration keys: " +
                string.Join(", ", missing) +
                ". Set them via .NET user-secrets or the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY environment variables.");
        }
    }
}
