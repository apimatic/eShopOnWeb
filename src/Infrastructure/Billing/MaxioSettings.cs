using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from configuration section "Maxio" (keys: ApiKey, Subdomain, ProductFamilyHandle, BaseUrl).
/// Values come from user-secrets / environment, never from a repository file.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API base address instead of deriving it from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Names of the invalid settings (never their values). Empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) problems.Add($"{SectionName}:{nameof(ApiKey)} is not configured");
        if (string.IsNullOrWhiteSpace(Subdomain)) problems.Add($"{SectionName}:{nameof(Subdomain)} is not configured");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) problems.Add($"{SectionName}:{nameof(ProductFamilyHandle)} is not configured");
        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            problems.Add($"{SectionName}:{nameof(BaseUrl)} must be an absolute http(s) URL");
        }
        return problems;
    }

    /// <summary>Maps MAXIO_ENVIRONMENT ("us" / "eu", case-insensitive, default us) to the SDK environment.</summary>
    public static ServerEnvironment ParseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("us", StringComparison.OrdinalIgnoreCase))
            return ServerEnvironment.Us;
        if (value.Trim().Equals("eu", StringComparison.OrdinalIgnoreCase))
            return ServerEnvironment.Eu;
        throw new InvalidOperationException("MAXIO_ENVIRONMENT must be 'us' or 'eu'");
    }
}
