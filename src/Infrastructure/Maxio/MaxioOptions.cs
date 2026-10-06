using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (used as the Basic auth username; password is literal "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Maxio (Advanced Billing) site.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Validates that all required settings are present.
    /// </summary>
    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"Maxio:{nameof(ApiKey)}");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"Maxio:{nameof(ProductFamilyHandle)}");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain)) missing.Add($"Maxio:{nameof(Subdomain)} (or Maxio:{nameof(BaseUrl)})");
        if (missing.Count > 0)
        {
            throw new MaxioConfigurationException($"Missing required Maxio configuration: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins when set;
    /// otherwise it is derived from the site subdomain per the OpenAPI server template.
    /// </summary>
    public string ResolveBaseUrl()
    {
        Validate();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return $"https://{Subdomain.Trim().TrimEnd('.')}.chargify.com";
    }
}