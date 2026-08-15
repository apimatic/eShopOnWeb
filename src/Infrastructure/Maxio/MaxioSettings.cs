namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied by the
/// host (user-secrets / environment) — never hard-coded — so the same build runs against any Maxio
/// site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>Maxio/Chargify API key. Sent as the HTTP Basic username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim as the Maxio base address instead
    /// of being derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio hosting region: <c>US</c> (default) or <c>EU</c>. Optional.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Payment collection method for card-free signups: <c>Invoice</c> (default) or <c>Remittance</c>.
    /// Configurable because the acceptable value depends on the site's billing architecture. Optional.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Optional handle of the plan to enroll in when a subscribe request does not name one. When unset,
    /// the service defaults to the highest-priced active plan in the product family.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }
}
