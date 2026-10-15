using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Returns the names of the settings that are missing or invalid (never their values).</summary>
    public string[] Validate()
    {
        var problems = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) problems.Add("Maxio:ApiKey is not configured");
        if (string.IsNullOrWhiteSpace(Subdomain)) problems.Add("Maxio:Subdomain is not configured");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) problems.Add("Maxio:ProductFamilyHandle is not configured");
        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            problems.Add("Maxio:BaseUrl is not an absolute http(s) URL");
        return problems.ToArray();
    }
}
