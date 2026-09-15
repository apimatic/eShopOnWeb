using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;

    public string Subdomain { get; init; } = string.Empty;

    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional complete Maxio Advanced Billing API base URL override.</summary>
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl.TrimEnd('/') + "/";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return uri;
    }
}
