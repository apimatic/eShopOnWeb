using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    // Populated from MAXIO_ENVIRONMENT as Maxio:Environment.
    // US and EU are the server environments defined by maxio-spec/openapi.yaml.
    public string Environment { get; set; } = "US";

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        }

        var host = Environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";

        return new Uri(host, UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        _ = GetBaseAddress();
    }
}
