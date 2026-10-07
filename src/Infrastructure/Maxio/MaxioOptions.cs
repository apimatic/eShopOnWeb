using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section; secrets must be provided via user-secrets
/// or environment variables, never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The site API key. Sent as the Basic-auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain (e.g. "acme" for acme.chargify.com).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Deployment environment: "US" or "EU".</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim
    /// instead of deriving one from the subdomain and environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins when set;
    /// otherwise the host is derived from the subdomain and environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.chargify.eu"
            : $"{Subdomain}.chargify.com";
        return $"https://{host}";
    }
}
