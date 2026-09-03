using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPal.Core.Models;

namespace PayPal.Models;

/// <summary>
/// The purchase unit request. Includes required information for the payment contract.
/// </summary>
public record PurchaseUnitRequest
{
    /// <summary>
    /// The API caller-provided external ID for the purchase unit. Required for multiple purchase units when you must update the order through <c>PATCH</c>. If you omit this value and the order contains only one purchase unit, PayPal sets this value to <c>default</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference_id")]
    [StringLength(256, MinimumLength = 1)]
    public string? ReferenceId { get; init; }

    /// <summary>
    /// The total order amount with an optional breakdown that provides details, such as the total item amount, total tax amount, shipping, handling, insurance, and discounts, if any. If you specify <c>amount.breakdown</c>, the amount equals <c>item_total</c> plus <c>tax_total</c> plus <c>shipping</c> plus <c>handling</c> plus <c>insurance</c> minus <c>shipping_discount</c> minus discount. The amount must be a positive number. For listed of supported currencies and decimal precision, see the PayPal REST APIs Currency Codes.
    /// </summary>
    [JsonPropertyName("amount")]
    public required AmountWithBreakdown Amount { get; init; }

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
    /// This field supports up to 3,000 characters, but any content beyond 127 characters (including spaces) will be truncated. The 127 character limit is reflected in the response representation of this field. The purchase description. The maximum length of the character is dependent on the type of characters used. The character length is specified assuming a US ASCII character. Depending on type of character; (e.g. accented character, Japanese characters) the number of characters that that can be specified as input might not equal the permissible max length.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    [StringLength(3000, MinimumLength = 1)]
    public string? Description { get; init; }

    /// <summary>
    /// The API caller-provided external ID. Used to reconcile client transactions with PayPal transactions. Appears in transaction and settlement reports but is not visible to the payer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_id")]
    [StringLength(255, MinimumLength = 1)]
    public string? CustomId { get; init; }

    /// <summary>
    /// The API caller-provided external invoice number for this order. Appears in both the payer's transaction history and the emails that the payer receives. invoice_id values are required to be unique within each merchant account by default. Although the uniqueness validation is configurable, disabling this behavior will remove the account's ability to use invoice_id in other APIs as an identifier. It is highly recommended to keep a unique invoice_id for each Order.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_id")]
    [StringLength(127, MinimumLength = 1)]
    public string? InvoiceId { get; init; }

    /// <summary>
    /// This field supports up to 127 characters, but any content beyond 22 characters (including spaces) will be truncated. The 22 character limit is reflected in the response representation of this field. The soft descriptor is the dynamic text used to construct the statement descriptor that appears on a payer's card statement. If an Order is paid using the "PayPal Wallet", the statement descriptor will appear in following format on the payer's card statement: PAYPAL_prefix+(space)+merchant_descriptor+(space)+ soft_descriptor Note: The merchant descriptor is the descriptor of the merchant’s payment receiving preferences which can be seen by logging into the merchant account https://www.sandbox.paypal.com/businessprofile/settings/info/edit The PAYPAL prefix uses 8 characters. Only the first 22 characters will be displayed in the statement. For example, if: The PayPal prefix toggle is PAYPAL *. The merchant descriptor in the profile is Janes Gift. The soft descriptor is 800-123-1234. Then, the statement descriptor on the card is PAYPAL * Janes Gift 80.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("soft_descriptor")]
    [StringLength(1000, MinimumLength = 1)]
    public string? SoftDescriptor { get; init; }

    /// <summary>
    /// An array of items that the customer purchases from the merchant.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<ItemRequest>? Items { get; init; }

    /// <summary>
    /// The shipping details.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping")]
    public ShippingDetails? Shipping { get; init; }

    /// <summary>
    /// Supplementary data about a payment. This object passes information that can be used to improve risk assessments and processing costs, for example, by providing Level 2 and Level 3 payment data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("supplementary_data")]
    public SupplementaryData? SupplementaryData { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
