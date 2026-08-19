using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment
/// variables / user-secrets — never from committed configuration.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API root. <see cref="BaseUrl"/> wins when set;
    /// otherwise the OpenAPI server template is applied (<c>https://{site}.chargify.com</c>
    /// for US, <c>https://{site}.ebilling.maxio.com</c> for EU).
    /// </summary>
    public Uri GetApiBaseAddress(string? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim().TrimEnd('/');
            return new Uri(trimmed + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var isEu = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase);
        var host = isEu ? $"{Subdomain}.ebilling.maxio.com" : $"{Subdomain}.chargify.com";
        return new Uri($"https://{host}/");
    }
}
