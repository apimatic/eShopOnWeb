namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the "Maxio" section. Values are supplied via
/// configuration/user-secrets only — never hard-coded — so the same build can target a different
/// Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, used to derive the base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> per the spec's US server template
    /// (<c>https://{site}.chargify.com</c>).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/') + "/";
        }

        return $"https://{Subdomain}.chargify.com/";
    }
}
