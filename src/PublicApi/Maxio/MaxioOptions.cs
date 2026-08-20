using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    public string? BaseUrl { get; init; }

    public Uri GetBaseUri() => string.IsNullOrWhiteSpace(BaseUrl)
        ? new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute)
        : new Uri(BaseUrl, UriKind.Absolute);
}
