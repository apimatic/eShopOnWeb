using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Configuration for the Maxio Advanced Billing site used by this API.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>An optional complete Advanced Billing API base address.</summary>
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(SectionName, typeof(MaxioOptions), new[] { "Maxio:BaseUrl must be an absolute HTTPS URL." });
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
