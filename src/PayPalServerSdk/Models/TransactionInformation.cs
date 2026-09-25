using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The transaction information.
/// </summary>
public record TransactionInformation
{
    /// <summary>
    /// The ID of the PayPal account of the counterparty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal_account_id")]
    [StringLength(24, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? PaypalAccountId { get; init; }

    /// <summary>
    /// The PayPal-generated transaction ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_id")]
    [StringLength(24, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? TransactionId { get; init; }

    /// <summary>
    /// The PayPal-generated base ID. PayPal exclusive. Cannot be altered. Defined as a related, pre-existing transaction or event.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal_reference_id")]
    [StringLength(24, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? PaypalReferenceId { get; init; }

    /// <summary>
    /// The PayPal reference ID type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("paypal_reference_id_type")]
    public PayPalReferenceIdType? PaypalReferenceIdType { get; init; }

    /// <summary>
    /// A five-digit transaction event code that classifies the transaction type based on money movement and debit or credit. For example, T0001. See <see href="/docs/integration/direct/transaction-search/transaction-event-codes/">Transaction event codes</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_event_code")]
    [StringLength(5, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? TransactionEventCode { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_initiation_date")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? TransactionInitiationDate { get; init; }

    /// <summary>
    /// The date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required while fractional seconds are optional. Note: The regular expression provides guidance but does not reject all invalid dates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_updated_date")]
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public string? TransactionUpdatedDate { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_amount")]
    public Money? TransactionAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fee_amount")]
    public Money? FeeAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("discount_amount")]
    public Money? DiscountAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("insurance_amount")]
    public Money? InsuranceAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sales_tax_amount")]
    public Money? SalesTaxAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_amount")]
    public Money? ShippingAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_discount_amount")]
    public Money? ShippingDiscountAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("shipping_tax_amount")]
    public Money? ShippingTaxAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("other_amount")]
    public Money? OtherAmount { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tip_amount")]
    public Money? TipAmount { get; init; }

    /// <summary>
    /// A code that indicates the transaction status. Value is: Status code Description D PayPal or merchant rules denied the transaction. P The transaction is pending. The transaction was created but waits for another payment process to complete, such as an ACH transaction, before the status changes to S. S The transaction successfully completed without a denial and after any pending statuses. V A successful transaction was fully reversed and funds were refunded to the original sender.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_status")]
    [StringLength(1, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? TransactionStatus { get; init; }

    /// <summary>
    /// The subject of payment. The payer passes this value to the payee. The payer controls this data through the interface through which he or she sends the data.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_subject")]
    [StringLength(256, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., \":;\\!?]*$")]
    public string? TransactionSubject { get; init; }

    /// <summary>
    /// A special note that the payer passes to the payee. Might contain special customer requests, such as shipping instructions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("transaction_note")]
    [StringLength(4000, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., \":;\\!?]*$")]
    public string? TransactionNote { get; init; }

    /// <summary>
    /// The payment tracking ID, which is a unique ID that partners specify to either get information about a payment or request a refund.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_tracking_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? PaymentTrackingId { get; init; }

    /// <summary>
    /// The bank reference ID. The bank provides this value for an ACH transaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bank_reference_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? BankReferenceId { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ending_balance")]
    public Money? EndingBalance { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("available_balance")]
    public Money? AvailableBalance { get; init; }

    /// <summary>
    /// The invoice ID that is sent by the merchant with the transaction. Note: If an invoice ID was sent with the capture request, the value is reported. Otherwise, the invoice ID of the authorizing transaction is reported.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("invoice_id")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., \":;\\!?]*$")]
    public string? InvoiceId { get; init; }

    /// <summary>
    /// The merchant-provided custom text. Note: Usually, this field includes the unique ID for payments made with MassPay type transaction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_field")]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9_'\\-., \":;\\!?]*$")]
    public string? CustomField { get; init; }

    /// <summary>
    /// Indicates whether the transaction is eligible for protection. Value is: 01. Eligible. 02. Not eligible 03. Partially eligible.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("protection_eligibility")]
    [StringLength(2, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9]*$")]
    public string? ProtectionEligibility { get; init; }

    /// <summary>
    /// The credit term. The time span covered by the installment payments as expressed in the term length plus the length time unit code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_term")]
    [StringLength(25, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9.]*$")]
    public string? CreditTerm { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_transactional_fee")]
    public Money? CreditTransactionalFee { get; init; }

    /// <summary>
    /// The currency and amount for a financial transaction, such as a balance or payment due.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("credit_promotional_fee")]
    public Money? CreditPromotionalFee { get; init; }

    /// <summary>
    /// The percentage, as a fixed-point, signed decimal number. For example, define a 19.99% interest rate as <c>19.99</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("annual_percentage_rate")]
    [RegularExpression("^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$")]
    public string? AnnualPercentageRate { get; init; }

    /// <summary>
    /// The payment method that was used for a transaction. Value is PUI, installment, or mEFT. Note: Appears only for pay upon invoice (PUI), installment, and mEFT transactions. Merchants and partners in the EMEA region can use this attribute to note transactions that attract turn-over tax.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("payment_method_type")]
    [StringLength(20, MinimumLength = 1)]
    [RegularExpression("^[a-zA-Z0-9-]*$")]
    public string? PaymentMethodType { get; init; }

    /// <summary>
    /// A high-level classification of the type of financial instrument that was used to fund a payment. The pattern is not provided because the value is defined by an external party. E.g. PAYPAL, CREDIT_CARD, DEBIT_CARD, APPLE_PAY, BANK , VENMO ,Pay Upon Invoice, Pay Later  or Alternative Payment Methods (APM).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instrument_type")]
    [StringLength(64, MinimumLength = 1)]
    public string? InstrumentType { get; init; }

    /// <summary>
    /// A finer-grained classification of the financial instrument that was used to fund a payment. For example, <c>Visa card</c> or a <c>Mastercard</c> for a credit card, BANKCARD ,DISCOVER etc. The pattern is not provided because the value is defined by an external party.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("instrument_sub_type")]
    [StringLength(64, MinimumLength = 1)]
    public string? InstrumentSubType { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
