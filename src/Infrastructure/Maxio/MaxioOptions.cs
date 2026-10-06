using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; sensitive values are supplied
/// via user-secrets or environment variables, never stored in the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Billing API key, used as the Basic-auth username (password is "X").
    /// Bound from Maxio:ApiKey.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Billing API site subdomain. Bound from Maxio:Subdomain.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the product family that contains the subscription plans
    /// exposed by this application. Bound from Maxio:ProductFamilyHandle.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the API base address. When set, it is used
    /// instead of the address derived from the subdomain. Bound from Maxio:BaseUrl.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Per the Billing API docs, the US environment
    /// is https://{site}.chargify.com; Maxio:BaseUrl, when present, is used instead.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!.TrimEnd('/') + "/");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}