using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress(string? environment)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!trimmed.EndsWith('/'))
            {
                trimmed += "/";
            }

            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return new Uri($"https://{host}/");
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }
    }
}
