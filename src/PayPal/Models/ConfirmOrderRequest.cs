using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// Payer confirms the intent to pay for the Order using the provided payment source.
/// </summary>
public record ConfirmOrderRequest
{
    /// <summary>
    /// The payment source definition.
    /// </summary>
    [JsonPropertyName("payment_source")]
    public required PaymentSource PaymentSource { get; init; }

    /// <summary>
    /// The instruction to process an order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_instruction")]
    public ProcessingInstruction? ProcessingInstruction { get; init; }

    /// <summary>
    /// Customizes the payer confirmation experience.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_context")]
    public OrderConfirmApplicationContext? ApplicationContext { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
