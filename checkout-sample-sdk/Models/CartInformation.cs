using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PayPalServerSdk.Models;

/// <summary>
/// The cart information.
/// </summary>
public record CartInformation
{
    /// <summary>
    /// An array of item details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("item_details")]
    [MinLength(1)]
    [MaxLength(32767)]
    public IReadOnlyList<ItemDetails>? ItemDetails { get; init; }

    /// <summary>
    /// Indicates whether the item amount or the shipping amount already includes tax.
    /// </summary>
    [JsonPropertyName("tax_inclusive")]
    public bool? TaxInclusive { get; init; } = false;

    /// <summary>
    /// The ID of the invoice. Appears for only PayPal-generated invoices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal_invoice_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., \":;\\!?]*$")]
    public string? PaypalInvoiceId { get; init; }
}
