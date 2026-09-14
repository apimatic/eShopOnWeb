using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing API. Bound from the "Maxio" configuration
/// section; values are supplied via user-secrets / environment and never hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key for the Maxio site ("Maxio:ApiKey").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain ("Maxio:Subdomain"), used to derive the base URL when
    /// <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Hosting environment for the Maxio site ("Maxio:Environment"): US or EU,
    /// per the OpenAPI spec server configuration.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Product family handle whose products are offered as subscription plans
    /// ("Maxio:ProductFamilyHandle").</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base URL override ("Maxio:BaseUrl"). When set it is
    /// used as-is instead of deriving the base URL from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins when set; otherwise the
    /// base URL is derived from the subdomain and environment per the OpenAPI spec's
    /// server templates (https://{site}.chargify.com for US,
    /// https://{site}.ebilling.maxio.com for EU).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var subdomain = Subdomain.Trim().TrimEnd('/');
        if (subdomain.Length == 0)
        {
            throw new InvalidOperationException(
                "Maxio base URL is not configured: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
    }
}
