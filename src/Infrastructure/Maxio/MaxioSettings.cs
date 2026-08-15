namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// Values are supplied via configuration/user-secrets/environment — never hard-coded — so the same
/// build runs against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the Basic-auth username). From <c>Maxio:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; the base address becomes <c>https://{Subdomain}.chargify.com</c>. From <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscribable plans. From <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim, overriding the address
    /// derived from <see cref="Subdomain"/>. From <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Plan handle used when a subscribe request omits one. Overridable via <c>Maxio:DefaultPlanHandle</c>
    /// so a different catalog can pick a different default target; defaults to the Pro plan.
    /// </summary>
    public string DefaultPlanHandle { get; set; } = "eshop-pro";
}
