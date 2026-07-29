namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied via environment/user-secrets — never hard-coded — so the same build can target a
/// different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>API key used as the Basic-auth username (password is the literal "x"). Key: <c>Maxio:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain. Key: <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscribable plans. Key: <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional API base-address override. When set, it is used verbatim as the base address instead of
    /// deriving one from <see cref="Subdomain"/>/<see cref="Environment"/>. Key: <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional hosting environment, <c>US</c> (default) or <c>EU</c>. Key: <c>Maxio:Environment</c>.</summary>
    public string? Environment { get; set; }
}
