using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing site.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public string? Subdomain { get; init; }

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>An optional full Advanced Billing API base URL.</summary>
    public string? BaseUrl { get; init; }

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }

        return !string.IsNullOrWhiteSpace(options.Subdomain);
    }

    public Uri GetBaseUri()
    {
        // Advanced Billing's documented US API base URI is https://{site}.chargify.com.
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        return new Uri(baseUrl!.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
