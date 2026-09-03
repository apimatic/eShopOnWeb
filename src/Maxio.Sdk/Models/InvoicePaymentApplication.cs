using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record InvoicePaymentApplication
{
    /// <summary>
    /// Unique identifier for the paid invoice. It has the prefix "inv_" followed by alphanumeric characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_uid")]
    public string? InvoiceUid { get; init; }

    /// <summary>
    /// Unique identifier for the payment. It has the prefix "pmt_" followed by alphanumeric characters.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_uid")]
    public string? ApplicationUid { get; init; }

    /// <summary>
    /// Dollar amount of the paid invoice.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applied_amount")]
    public string? AppliedAmount { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
