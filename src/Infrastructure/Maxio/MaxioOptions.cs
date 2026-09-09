using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section; values come from user-secrets / environment variables and are never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (used as the Basic-auth username; password is the literal "x" per the OpenAPI spec).
    /// Bound from Maxio:ApiKey.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain, substituted into the spec's server URL template https://{site}.chargify.com.
    /// Bound from Maxio:Subdomain.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// Bound from Maxio:ProductFamilyHandle.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override. When set, it is used instead of the
    /// URL derived from the subdomain. Bound from Maxio:BaseUrl.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetEffectiveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:BaseUrl or Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
        }

        return $"https://{Subdomain.TrimEnd('.')}.chargify.com";
    }
}
