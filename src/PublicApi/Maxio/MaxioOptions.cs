using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing API. The default endpoint follows the
/// US production server template in maxio-spec/openapi.yaml; BaseUrl is an optional
/// verbatim override for another Maxio server.
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

    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredUri))
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URI.");
            }

            return configuredUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? configuredUri
                : new Uri(configuredUri.AbsoluteUri + "/", UriKind.Absolute);
        }

        // Maxio OpenAPI's default US production server is https://{site}.chargify.com.
        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
