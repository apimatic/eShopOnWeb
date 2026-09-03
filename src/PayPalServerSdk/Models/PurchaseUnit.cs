using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Models;

/// <summary>
/// The purchase unit details. Used to capture required information for the payment contract.
/// </summary>
public record PurchaseUnit
{
    /// <summary>
    /// The API caller-provided external ID for the purchase unit. Required for multiple purchase units when you must update the order through <c>PATCH</c>. If you omit this value and the order contains only one purchase unit, PayPal sets this value to <c>default</c>. Note: If there are multiple purchase units, reference_id is required for each purchase unit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference_id")]
    [StringLength(256, MinimumLength = 1)]
    public string? ReferenceId { get; init; }

    /// <summary>
    /// The total order amount with an optional breakdown that provides details, such as the total item amount, total tax amount, shipping, handling, insurance, and discounts, if any. If you specify <c>amount.breakdown</c>, the amount equals <c>item_total</c> plus <c>tax_total</c> plus <c>shipping</c> plus <c>handling</c> plus <c>insurance</c> minus <c>shipping_discount</c> minus discount. The amount must be a positive number. For listed of supported currencies and decimal precision, see the PayPal REST APIs Currency Codes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("amount")]
    public AmountWithBreakdown? Amount { get; init; }

    /// <summary>
    /// The merchant who receives the funds and fulfills the order. The merchant is also known as the payee.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payee")]
    public PayeeBase? Payee { get; init; }

    /// <summary>
    /// Any additional payment instructions to be consider during payment processing. This processing instruction is applicable for Capturing an order or Authorizing an Order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_instruction")]
    public PaymentInstruction? PaymentInstruction { get; init; }

    /// <summary>
    /// The purchase description.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(127, MinimumLength = 1)]
    public string? Description { get; init; }

    /// <summary>
    /// The API caller-provided external ID. Used to reconcile API caller-initiated transactions with PayPal transactions. Appears in transaction and settlement reports.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_id")]
    [StringLength(255, MinimumLength = 1)]
    public string? CustomId { get; init; }

    /// <summary>
    /// The API caller-provided external invoice ID for this order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_id")]
    [StringLength(127, MinimumLength = 1)]
    public string? InvoiceId { get; init; }

    /// <summary>
    /// The PayPal-generated ID for the purchase unit. This ID appears in both the payer's transaction history and the emails that the payer receives. In addition, this ID is available in transaction and settlement reports that merchants and API callers can use to reconcile transactions. This ID is only available when an order is saved by calling v2/checkout/orders/id/save.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    [StringLength(19, MinimumLength = 1)]
    public string? Id { get; init; }

    /// <summary>
    /// The payment descriptor on account transactions on the customer's credit card statement, that PayPal sends to processors. The maximum length of the soft descriptor information that you can pass in the API field is 22 characters, in the following format:22 - len(PAYPAL * (8)) - len(Descriptor in Payment Receiving Preferences of Merchant account + 1)The PAYPAL prefix uses 8 characters. The soft descriptor supports the following ASCII characters: Alphanumeric characters Dashes Asterisks Periods (.) Spaces For Wallet payments marketplace integrations: The merchant descriptor in the Payment Receiving Preferences must be the marketplace name. You can't use the remaining space to show the customer service number. The remaining spaces can be a combination of seller name and country. For unbranded payments (Direct Card) marketplace integrations, use a combination of the seller name and phone number.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("soft_descriptor")]
    [StringLength(22, MinimumLength = 1)]
    public string? SoftDescriptor { get; init; }

    /// <summary>
    /// An array of items that the customer purchases from the merchant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<Item>? Items { get; init; }

    /// <summary>
    /// The order shipping details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping")]
    public ShippingWithTrackingDetails? Shipping { get; init; }

    /// <summary>
    /// Supplementary data about a payment. This object passes information that can be used to improve risk assessments and processing costs, for example, by providing Level 2 and Level 3 payment data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("supplementary_data")]
    public SupplementaryData? SupplementaryData { get; init; }

    /// <summary>
    /// The collection of payments, or transactions, for a purchase unit in an order. For example, authorized payments, captured payments, and refunds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payments")]
    public PaymentCollection? Payments { get; init; }

    /// <summary>
    /// The error reason code and description that are the reason for the most recent order decline.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("most_recent_errors")]
    [MinLength(1)]
    [MaxLength(10)]
    public IReadOnlyList<object>? MostRecentErrors { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
