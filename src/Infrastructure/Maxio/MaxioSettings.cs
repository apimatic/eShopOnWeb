using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section. No values live in the repository.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Maxio:ApiKey). Secret: user-secrets or environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain (Maxio:Subdomain).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose plans are offered (Maxio:ProductFamilyHandle).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Returns one message per problem; each names the key, never a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) problems.Add($"{SectionName}:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(Subdomain)) problems.Add($"{SectionName}:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) problems.Add($"{SectionName}:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            problems.Add($"{SectionName}:BaseUrl must be an absolute http(s) URL when set.");
        }
        return problems;
    }
}
