using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is required.");

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL.");
    }
}
