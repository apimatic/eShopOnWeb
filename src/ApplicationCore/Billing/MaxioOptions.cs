using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values must come from environment /
/// user-secrets — never from source-controlled files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the Advanced Billing API base URL from the OpenAPI server templates.
    /// When <see cref="BaseUrl"/> is set it is used verbatim (normalized with a trailing slash).
    /// Otherwise the US host <c>https://{site}.chargify.com</c> or the EU host
    /// <c>https://{site}.ebilling.maxio.com</c> is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string ResolveApiBaseUrl(string? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var host = IsEuHost(environment) ? "ebilling.maxio.com" : "chargify.com";
        return $"https://{Subdomain.Trim()}.{host}/";
    }

    public static bool IsEuHost(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return false;
        }

        return environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
               || environment.Equals("ebilling", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }
}
