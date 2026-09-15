using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details sent by the shopper to pay a one-off order or to save a card. These are passed
/// straight to PayPal and are NEVER stored in this application's database nor written to logs.
/// </summary>
public class CardModel
{
    /// <summary>Primary account number (13–19 digits). e.g. the sandbox test Visa 4111111111111111.</summary>
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in ISO-8601 YYYY-MM form, e.g. "2030-01".</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card verification value (3–4 digits).</summary>
    [Required]
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name as printed on the card.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>ISO-3166-1 alpha-2 country code, e.g. "US". Required by PayPal for a card billing address.</summary>
    [Required]
    public string CountryCode { get; set; } = "US";

    public CardDetails ToCardDetails() => new(
        Number: Number,
        Expiry: Expiry,
        SecurityCode: SecurityCode,
        CardholderName: Name,
        Line1: Line1,
        Line2: Line2,
        City: City,
        State: State,
        PostalCode: PostalCode,
        CountryCode: CountryCode);
}
