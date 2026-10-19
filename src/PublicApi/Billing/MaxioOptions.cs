using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from user-secrets in
/// development and from the environment / a secret store in deployment — never from appsettings files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (the HTTP Basic username).</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain; used to derive the API base address.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional. When set, used verbatim as the API base address instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    internal static bool HasValidBaseUrl(MaxioOptions options) =>
        string.IsNullOrWhiteSpace(options.BaseUrl)
        || (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
}
