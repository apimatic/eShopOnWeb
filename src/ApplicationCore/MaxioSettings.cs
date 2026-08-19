using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from environment
/// variables / user-secrets — never from source.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Advanced Billing API root. <see cref="BaseUrl"/> wins when set;
    /// otherwise the site is derived from <see cref="Subdomain"/> and MAXIO_ENVIRONMENT
    /// (EU → ebilling.maxio.com, otherwise chargify.com).
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Configure Maxio:BaseUrl or Maxio:Subdomain.");
        }

        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var host = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{host}/";
    }
}
