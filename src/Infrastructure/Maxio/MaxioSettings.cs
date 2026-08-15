using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied by
/// configuration/user-secrets/environment — never hard-coded — so the same build can target a
/// different Maxio site and catalog. <see cref="BaseUrl"/> is an optional override: when set it
/// is used verbatim as the API base address instead of deriving one from <see cref="Subdomain"/>.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (HTTP Basic username; the password is the literal "x").</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain; resolves the base address <c>https://{Subdomain}.chargify.com</c> unless <see cref="BaseUrl"/> overrides it.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribe-able plans.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional explicit API base URL; when non-empty it wins over the subdomain-derived address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method used when enrolling a shopper without capturing a payment method. Defaults
    /// to <c>remittance</c> (invoice-based, no card required — the card-less path on Relationship
    /// Invoicing sites). Set to <c>invoice</c> for legacy Statements sites, or empty to let Maxio
    /// default (which requires a payment method).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; } = "remittance";
}
