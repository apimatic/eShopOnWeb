using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Subdomain) || string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            error = "Maxio configuration requires ApiKey, Subdomain, and ProductFamilyHandle.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            error = "Maxio BaseUrl must be an absolute URL when configured.";
            return false;
        }

        error = null;
        return true;
    }
}
