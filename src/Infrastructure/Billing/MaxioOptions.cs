using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values are supplied via user-secrets or
/// the <c>MAXIO_*</c> environment variables — never from source.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the Billing API base address instead of
    /// <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    public bool TryGetBaseAddress(out Uri baseAddress)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim().TrimEnd('/') + "/";
            return Uri.TryCreate(trimmed, UriKind.Absolute, out baseAddress!);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            baseAddress = null!;
            return false;
        }

        return Uri.TryCreate($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute, out baseAddress!);
    }
}
