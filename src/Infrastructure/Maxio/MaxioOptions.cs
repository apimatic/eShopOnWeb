namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration/user-secrets,
/// never be hard-coded, so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (site-scoped). Used as the Basic-Auth username; password is "x".</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "cp-exp-4". Used to derive the base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address, used verbatim instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }
}
