namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration/user-secrets/
/// environment variables - never hard-code a site's credentials or catalog here.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Used as the Basic-Auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. "acme" for "acme.chargify.com".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of deriving
    /// "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl)
        ? $"https://{Subdomain}.chargify.com"
        : BaseUrl!;
}
