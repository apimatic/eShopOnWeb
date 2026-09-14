namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values come from user-secrets/environment in
/// Development and from the real hosting configuration provider in other environments - never
/// hard-code a value here, since the same build must run against a different Maxio site/catalog.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim override of the API base address. When set, used as-is instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }
}
