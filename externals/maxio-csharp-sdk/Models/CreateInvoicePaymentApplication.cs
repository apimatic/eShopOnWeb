using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CreateInvoicePaymentApplication
{
    /// <summary>
    /// Unique identifier for the invoice. It has the prefix "inv_" followed by alphanumeric characters.
    /// </summary>
    [JsonPropertyName("invoice_uid")]
    public required string InvoiceUid { get; init; }

    /// <summary>
    /// Dollar amount of the invoice payment (eg. "10.50" =&gt; $10.50).
    /// </summary>
    [JsonPropertyName("amount")]
    public required string Amount { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
