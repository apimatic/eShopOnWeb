using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The order authorize response.
/// </summary>
public record OrderAuthorizeResponse
{
    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("create_time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? CreateTime { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("update_time")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? UpdateTime { get; init; }

    /// <summary>
    /// The ID of the order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The payment source used to fund the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_source")]
    public OrderAuthorizeResponsePaymentSource? PaymentSource { get; init; }

    /// <summary>
    /// The intent to either capture payment immediately or authorize a payment for an order after order creation.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("intent")]
    public CheckoutPaymentIntent? Intent { get; init; }

    /// <summary>
    /// The instruction to process an order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("processing_instruction")]
    public ProcessingInstruction? ProcessingInstruction { get; init; }

    /// <summary>
    /// The customer who approves and pays for the order. The customer is also known as the payer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payer")]
    public Payer? Payer { get; init; }

    /// <summary>
    /// An array of purchase units. Each purchase unit establishes a contract between a customer and merchant. Each purchase unit represents either a full or partial order that the customer intends to purchase from the merchant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("purchase_units")]
    [MinLength(1)]
    [MaxLength(10)]
    public IReadOnlyList<PurchaseUnit>? PurchaseUnits { get; init; }

    /// <summary>
    /// The order status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public OrderStatus? Status { get; init; }

    /// <summary>
    /// An array of request-related HATEOAS links. To complete payer approval, use the <c>approve</c> link to redirect the payer. The API caller has 6 hours (default setting, this which can be changed by your account manager to 24/48/72 hours to accommodate your use case) from the time the order is created, to redirect your payer. Once redirected, the API caller has 6 hours for the payer to approve the order and either authorize or capture the order. If you are not using the PayPal JavaScript SDK to initiate PayPal Checkout (in context) ensure that you include <c>application_context.return_url</c> is specified or you will get "We're sorry, Things don't appear to be working at the moment" after the payer approves the payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkDescription>? Links { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
