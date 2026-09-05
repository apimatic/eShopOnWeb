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

    public string GetBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl)
        ? $"https://{Subdomain}.chargify.com/"
        : BaseUrl;

    public bool HasValidBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl)
        || (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
}
