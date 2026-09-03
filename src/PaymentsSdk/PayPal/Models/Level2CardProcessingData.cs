using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The level 2 card processing data collections. If your merchant account has been configured for Level 2 processing this field will be passed to the processor on your behalf. Please contact your PayPal Technical Account Manager to define level 2 data for your business.
/// </summary>
public record Level2CardProcessingData
{
    /// <summary>
    /// Use this field to pass a purchase identification value of up to 127 ASCII characters. The length of this field will be adjusted to meet network specifications (25chars for Visa and Mastercard, 17chars for Amex), and the original invoice ID will still be displayed in your existing reports.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[\\w‘\\-.,\":;\\!?]*$")]
    public string? InvoiceId { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_total")]
    public Money? TaxTotal { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
