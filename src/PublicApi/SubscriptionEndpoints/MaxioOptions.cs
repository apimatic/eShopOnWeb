using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Configuration for the Maxio Advanced Billing site that owns subscription state.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional API base URL override, for example an EU-hosted Billing API site.</summary>
    public string? BaseUrl { get; init; }

    public bool HasValidBaseAddress()
    {
        return string.IsNullOrWhiteSpace(BaseUrl)
            || Uri.TryCreate(BaseUrl, UriKind.Absolute, out var address)
                && address.Scheme == Uri.UriSchemeHttps;
    }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.EndsWith('/') ? BaseUrl : $"{BaseUrl}/", UriKind.Absolute);
        }

        // The Billing API documentation specifies this direct-host format for US sandbox sites.
        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
