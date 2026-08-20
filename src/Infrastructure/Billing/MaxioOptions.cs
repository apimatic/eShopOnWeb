using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from user-secrets or environment
/// variables — never from committed source.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Billing API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Billing API origin. <see cref="BaseUrl"/> wins when present; otherwise the
    /// US-hosted origin <c>https://{subdomain}.chargify.com</c> is used.
    /// </summary>
    public Uri GetApiBaseAddress()
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

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }
}
