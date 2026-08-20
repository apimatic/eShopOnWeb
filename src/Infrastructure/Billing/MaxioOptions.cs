using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment
/// variables / user-secrets — never from committed files.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return EnsureTrailingSlash(BaseUrl.Trim());
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        // US Advanced Billing host. EU sites should set Maxio:BaseUrl to
        // https://{site}.ebilling.maxio.com instead of relying on this default.
        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
