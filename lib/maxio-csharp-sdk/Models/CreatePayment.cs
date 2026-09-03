using System.Text.Json.Serialization;
using Maxio.Core.Models;
using Maxio.Models.Enums;

namespace Maxio.Models;

public record CreatePayment
{
    [JsonPropertyName("amount")]
    public required string Amount { get; init; }

    [JsonPropertyName("memo")]
    public required string Memo { get; init; }

    [JsonPropertyName("payment_details")]
    public required string PaymentDetails { get; init; }

    /// <summary>
    /// The type of payment method used. Defaults to other.
    /// </summary>
    [JsonPropertyName("payment_method")]
    public required InvoicePaymentMethodType PaymentMethod { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
