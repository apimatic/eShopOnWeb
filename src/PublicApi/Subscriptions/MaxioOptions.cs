using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Subdomain { get; set; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional complete API base address. When empty, it is derived from Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return CreateBaseUri(BaseUrl);
        }

        if (Subdomain.IndexOfAny(new[] { '/', '\\', '?', '#', ':' }) >= 0)
        {
            throw new InvalidOperationException("Maxio:Subdomain must be a subdomain, not a URL.");
        }

        return CreateBaseUri($"https://{Subdomain}.chargify.com");
    }

    private static Uri CreateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
