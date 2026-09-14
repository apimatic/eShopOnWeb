namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values are sourced from environment/user-secrets;
/// none are hard-coded so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (Basic Auth username; password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "cp-exp-3" for https://cp-exp-3.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Maxio API base address. When set, used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl) ? $"https://{Subdomain}.chargify.com" : BaseUrl!;
}
