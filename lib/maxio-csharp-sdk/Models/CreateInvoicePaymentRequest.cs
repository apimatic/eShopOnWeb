using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreateInvoicePaymentRequest
{
    [JsonPropertyName("payment")]
    public required CreateInvoicePayment Payment { get; init; }

    /// <summary>
    /// The type of payment to be applied to an Invoice. Defaults to external.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("type")]
    public InvoicePaymentType? Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
