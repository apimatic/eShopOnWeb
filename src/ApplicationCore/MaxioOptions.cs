using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Credential values come from
/// environment variables / user-secrets — never from source control.
/// </summary>
public class MaxioOptions
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

    public string ResolveBaseUrl(string? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        // Official SDK environments: US => https://{site}.chargify.com, EU => https://{site}.ebilling.maxio.com
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return $"https://{host}/";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
