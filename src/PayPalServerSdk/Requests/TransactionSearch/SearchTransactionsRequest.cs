using System.ComponentModel.DataAnnotations;
using PayPalServerSdk.Core.Validation.Attributes;

namespace PayPalServerSdk.Requests.TransactionSearch;

/// <summary>
/// The inputs of the SearchTransactions operation.
/// </summary>
public sealed record SearchTransactionsRequest
{
    /// <summary>
    /// Filters the transactions in the response by a start date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required. Fractional seconds are optional.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public required string StartDate { get; init; }

    /// <summary>
    /// Filters the transactions in the response by an end date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required. Fractional seconds are optional. The maximum supported range is 31 days.
    /// </summary>
    [StringLength(64, MinimumLength = 20)]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[1-2][0-9]|3[0-1])[T,t]([0-1][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)([.][0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    public required string EndDate { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a PayPal transaction ID. A valid transaction ID is 17 characters long, except for an order ID, which is 19 characters long. Note: A transaction ID is not unique in the reporting system. The response can list two transactions with the same ID. One transaction can be balance affecting while the other is non-balance affecting.
    /// </summary>
    [StringLength(19, MinimumLength = 17)]
    public string? TransactionId { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a PayPal transaction event code. See <see href="/docs/integration/direct/transaction-search/transaction-event-codes/">Transaction event codes</see>.
    /// </summary>
    public string? TransactionType { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a PayPal transaction status code. Value is: Status code Description D PayPal or merchant rules denied the transaction. P The transaction is pending. The transaction was created but waits for another payment process to complete, such as an ACH transaction, before the status changes to S. S The transaction successfully completed without a denial and after any pending statuses. V A successful transaction was reversed and funds were refunded to the original sender.
    /// </summary>
    public string? TransactionStatus { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a gross transaction amount range. Specify the range as <c> TO </c>, where <c> </c> is the lower limit of the gross PayPal transaction amount and <c> </c> is the upper limit of the gross transaction amount. Specify the amounts in lower denominations. For example, to search for transactions from $5.00 to $10.05, specify <c>[500 TO 1005]</c>. Note:The values must be URL encoded.
    /// </summary>
    public string? TransactionAmount { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> for the PayPal transaction currency.
    /// </summary>
    public string? TransactionCurrency { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a payment instrument type. Value is either: CREDITCARD. Returns a direct credit card transaction with a corresponding value. DEBITCARD. Returns a debit card transaction with a corresponding value. If you omit this parameter, the API does not apply this filter.
    /// </summary>
    public string? PaymentInstrumentType { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a store ID.
    /// </summary>
    public string? StoreId { get; init; }

    /// <summary>
    /// Filters the transactions in the response by a terminal ID.
    /// </summary>
    public string? TerminalId { get; init; }

    /// <summary>
    /// Indicates which fields appear in the response. Value is a single field or a comma-separated list of fields. The transaction_info value returns only the transaction details in the response. To include all fields in the response, specify fields=all. Valid fields are: transaction_info. The transaction information. Includes the ID of the PayPal account of the payee, the PayPal-generated transaction ID, the PayPal-generated base ID, the PayPal reference ID type, the transaction event code, the date and time when the transaction was initiated and was last updated, the transaction amounts including the PayPal fee, any discounts, insurance, the transaction status, and other information about the transaction. payer_info. The payer information. Includes the PayPal customer account ID and the payer's email address, primary phone number, name, country code, address, and whether the payer is verified or unverified. shipping_info. The shipping information. Includes the recipient's name, the shipping method for this order, the shipping address for this order, and the secondary address associated with this order. auction_info. The auction information. Includes the name of the auction site, the auction site URL, the ID of the customer who makes the purchase in the auction, and the date and time when the auction closes. cart_info. The cart information. Includes an array of item details, whether the item amount or the shipping amount already includes tax, and the ID of the invoice for PayPal-generated invoices. incentive_info. An array of incentive detail objects. Each object includes the incentive, such as a special offer or coupon, the incentive amount, and the incentive program code that identifies a merchant loyalty or incentive program. store_info. The store information. Includes the ID of the merchant store and the terminal ID for the checkout stand in the merchant store.
    /// </summary>
    public string Fields { get; init; } = "transaction_info";

    /// <summary>
    /// Indicates whether the response includes only balance-impacting transactions or all transactions. Value is either: Y. The default. The response includes only balance transactions. N. The response includes all transactions.
    /// </summary>
    public string BalanceAffectingRecordsOnly { get; init; } = "Y";

    /// <summary>
    /// The number of items to return in the response. So, the combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items. The combination of <c>page=2</c> and <c>page_size=20</c> returns the next 20 items.
    /// </summary>
    [Minimum(1)]
    [Maximum(500)]
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// The zero-relative start index of the entire list of items that are returned in the response. So, the combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items.
    /// </summary>
    [Minimum(1)]
    [Maximum(2147483647)]
    public int Page { get; init; } = 1;
}
