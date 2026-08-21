using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Maxio Advanced Billing settings bound from the <c>Maxio</c> configuration section.
/// Values come from environment variables / user-secrets — never from committed config.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Billing API base address. <see cref="BaseUrl"/> is used verbatim when set;
    /// otherwise the address is derived from <see cref="Subdomain"/> using the US Chargify host.
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }

        return DeriveApiBaseUrl(Subdomain, environment: null);
    }

    /// <summary>
    /// US sites use <c>https://{subdomain}.chargify.com</c>; EU-hosted sites use
    /// <c>https://{subdomain}.ebilling.maxio.com</c>.
    /// </summary>
    public static string DeriveApiBaseUrl(string subdomain, string? environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdomain);
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{subdomain}.ebilling.maxio.com"
            : $"{subdomain}.chargify.com";
        return $"https://{host}";
    }
}
