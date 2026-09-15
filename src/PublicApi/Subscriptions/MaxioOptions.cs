using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing site.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public static Uri GetBaseUri(MaxioOptions options)
    {
        var value = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? $"https://{options.Subdomain}.chargify.com/"
            : options.BaseUrl;

        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }
}
