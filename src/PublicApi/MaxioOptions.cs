using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Connection settings for Maxio Advanced Billing. Values are supplied through user secrets or environment configuration.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>An optional complete Maxio Billing API base URL override.</summary>
    public string? BaseUrl { get; init; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }
}
