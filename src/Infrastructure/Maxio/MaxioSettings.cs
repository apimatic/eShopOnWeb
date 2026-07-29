namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration
/// section. Values are supplied via user-secrets / environment configuration — never committed.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain; substituted into the API host template.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base-URL override. When set it is used verbatim as the base address;
    /// when empty the address is derived from <see cref="Subdomain"/> and the environment template.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional payment collection method for new subscriptions. Defaults to <c>remittance</c>
    /// (bill by invoice, no card capture) which suits payment-method-not-required plans on
    /// Relationship-Invoicing sites. Set to <c>invoice</c> for legacy Statements sites, or
    /// <c>automatic</c>/<c>prepaid</c> where a payment method is collected.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
