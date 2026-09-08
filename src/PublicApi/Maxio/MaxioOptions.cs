using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are never hard-coded:
/// ApiKey, Subdomain and ProductFamilyHandle come from the MAXIO_* environment
/// variables (see Program.cs), BaseUrl is an optional verbatim override.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. When <see cref="BaseUrl"/> is set it is used
    /// verbatim; otherwise one is derived from the site subdomain using the default
    /// "production" server template from the Maxio OpenAPI specification.
    /// </summary>
    public string GetApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var subdomain = Subdomain.Trim();
        return $"https://{subdomain}.chargify.com";
    }
}
