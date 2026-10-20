using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the <c>Maxio</c> configuration section. No defaults: every deployment supplies its own.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public static bool IsValid(MaxioSettings s, out string error)
    {
        if (string.IsNullOrWhiteSpace(s.ApiKey)) { error = "Maxio:ApiKey is not configured."; return false; }
        if (string.IsNullOrWhiteSpace(s.Subdomain)) { error = "Maxio:Subdomain is not configured."; return false; }
        if (string.IsNullOrWhiteSpace(s.ProductFamilyHandle)) { error = "Maxio:ProductFamilyHandle is not configured."; return false; }
        if (!string.IsNullOrWhiteSpace(s.BaseUrl) && !Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out _))
        {
            error = "Maxio:BaseUrl must be an absolute URL when set.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
