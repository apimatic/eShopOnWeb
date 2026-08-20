using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set, used instead of deriving a URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && TryResolveBaseUrl() is not null;

    public string ResolveBaseUrl()
    {
        var resolved = TryResolveBaseUrl();
        if (resolved is null)
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:BaseUrl or Maxio:Subdomain (and Maxio:ApiKey).");
        }

        return resolved;
    }

    public string? TryResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return null;
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        return trimmed;
    }
}
