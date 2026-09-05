using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing API. The API key belongs in user-secrets
/// (or a production secret store), never in an appsettings file.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional absolute API base URL. When omitted, the US server from the supplied OpenAPI document is used.</summary>
    public string? BaseUrl { get; init; }

    public Uri GetApiBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(SectionName, typeof(MaxioOptions), new[] { "Maxio:BaseUrl must be an absolute HTTPS URL." });
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
