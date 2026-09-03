using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The order request details.
/// </summary>
public record OrderRequest
{
    /// <summary>
    /// The intent to either capture payment immediately or authorize a payment for an order after order creation.
    /// </summary>
    [JsonPropertyName("intent")]
    public required CheckoutPaymentIntent Intent { get; init; }

    /// <summary>
    /// The instruction to process an order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_instruction")]
    public ProcessingInstruction? ProcessingInstruction { get; init; }

    /// <summary>
    /// DEPRECATED. The customer is also known as the payer. The Payer object was intended to only be used with the <c>payment_source.paypal</c> object. In order to make this design more clear, the details in the <c>payer</c> object are now available under <c>payment_source.paypal</c>. Please use <c>payment_source.paypal</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payer")]
    public Payer? Payer { get; init; }

    /// <summary>
    /// An array of purchase units. Each purchase unit establishes a contract between a payer and the payee. Each purchase unit represents either a full or partial order that the payer intends to purchase from the payee.
    /// </summary>
    [JsonPropertyName("purchase_units")]
    [MinLength(1)]
    [MaxLength(10)]
    public required IReadOnlyList<PurchaseUnitRequest> PurchaseUnits { get; init; }

    /// <summary>
    /// The payment source definition.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_source")]
    public PaymentSource? PaymentSource { get; init; }

    /// <summary>
    /// Customizes the payer experience during the approval process for the payment with PayPal. Note: Partners and Marketplaces might configure brand_name and shipping_preference during partner account setup, which overrides the request values.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("application_context")]
    public OrderApplicationContext? ApplicationContext { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
