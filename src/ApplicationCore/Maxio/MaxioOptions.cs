namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via user-secrets/environment
/// variables in every environment - never hard-code the values for these keys.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key, used as the Basic-Auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain, e.g. "cp-exp-3" for https://cp-exp-3.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the Product Family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, used verbatim instead of deriving one from
    /// <see cref="Subdomain"/> (e.g. to target an EU-hosted site or a non-default domain).
    /// </summary>
    public string? BaseUrl { get; set; }
}
