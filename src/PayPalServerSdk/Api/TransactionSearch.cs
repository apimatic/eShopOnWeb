using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Request;
using PayPalServerSdk.Core.Response;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Api;

/// <summary>
/// Use the <c>/transactions</c> resource to list transactions and the <c>/balances</c> resource to list balances.
/// </summary>
public sealed class TransactionSearch
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TransactionSearch(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// List all balances
    /// </summary>
    /// <param name="asOfTime">List balances in the response at the date time provided, will return the last refreshed balance in the system when not provided.</param>
    /// <param name="currencyCode">Filters the transactions in the response by a <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> for the PayPal transaction currency.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BalancesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SearchBalancesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all balances. Specify date time to list balances for that time that appear in the response. Notes: It takes a maximum of three hours for balances to appear in the list balances call. This call lists balances upto the previous three years.
    /// </remarks>
    public Task<BalancesResponse> SearchBalances(string? asOfTime,
        string? currencyCode,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/reporting/balances"),
            [],
            [new Param("as_of_time", asOfTime), new Param("currency_code", currencyCode)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BalancesResponse>(),
            SearchBalancesErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// List transactions
    /// </summary>
    /// <param name="startDate">Filters the transactions in the response by a start date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required. Fractional seconds are optional.</param>
    /// <param name="endDate">Filters the transactions in the response by an end date and time, in <see href="https://tools.ietf.org/html/rfc3339#section-5.6">Internet date and time format</see>. Seconds are required. Fractional seconds are optional. The maximum supported range is 31 days.</param>
    /// <param name="transactionId">Filters the transactions in the response by a PayPal transaction ID. A valid transaction ID is 17 characters long, except for an order ID, which is 19 characters long. Note: A transaction ID is not unique in the reporting system. The response can list two transactions with the same ID. One transaction can be balance affecting while the other is non-balance affecting.</param>
    /// <param name="transactionType">Filters the transactions in the response by a PayPal transaction event code. See <see href="/docs/integration/direct/transaction-search/transaction-event-codes/">Transaction event codes</see>.</param>
    /// <param name="transactionStatus">Filters the transactions in the response by a PayPal transaction status code. Value is: Status code Description D PayPal or merchant rules denied the transaction. P The transaction is pending. The transaction was created but waits for another payment process to complete, such as an ACH transaction, before the status changes to S. S The transaction successfully completed without a denial and after any pending statuses. V A successful transaction was reversed and funds were refunded to the original sender.</param>
    /// <param name="transactionAmount">Filters the transactions in the response by a gross transaction amount range. Specify the range as <c> TO </c>, where <c> </c> is the lower limit of the gross PayPal transaction amount and <c> </c> is the upper limit of the gross transaction amount. Specify the amounts in lower denominations. For example, to search for transactions from $5.00 to $10.05, specify <c>[500 TO 1005]</c>. Note:The values must be URL encoded.</param>
    /// <param name="transactionCurrency">Filters the transactions in the response by a <see href="https://developer.paypal.com/api/rest/reference/currency-codes/">three-character ISO-4217 currency code</see> for the PayPal transaction currency.</param>
    /// <param name="paymentInstrumentType">Filters the transactions in the response by a payment instrument type. Value is either: CREDITCARD. Returns a direct credit card transaction with a corresponding value. DEBITCARD. Returns a debit card transaction with a corresponding value. If you omit this parameter, the API does not apply this filter.</param>
    /// <param name="storeId">Filters the transactions in the response by a store ID.</param>
    /// <param name="terminalId">Filters the transactions in the response by a terminal ID.</param>
    /// <param name="fields">Indicates which fields appear in the response. Value is a single field or a comma-separated list of fields. The transaction_info value returns only the transaction details in the response. To include all fields in the response, specify fields=all. Valid fields are: transaction_info. The transaction information. Includes the ID of the PayPal account of the payee, the PayPal-generated transaction ID, the PayPal-generated base ID, the PayPal reference ID type, the transaction event code, the date and time when the transaction was initiated and was last updated, the transaction amounts including the PayPal fee, any discounts, insurance, the transaction status, and other information about the transaction. payer_info. The payer information. Includes the PayPal customer account ID and the payer's email address, primary phone number, name, country code, address, and whether the payer is verified or unverified. shipping_info. The shipping information. Includes the recipient's name, the shipping method for this order, the shipping address for this order, and the secondary address associated with this order. auction_info. The auction information. Includes the name of the auction site, the auction site URL, the ID of the customer who makes the purchase in the auction, and the date and time when the auction closes. cart_info. The cart information. Includes an array of item details, whether the item amount or the shipping amount already includes tax, and the ID of the invoice for PayPal-generated invoices. incentive_info. An array of incentive detail objects. Each object includes the incentive, such as a special offer or coupon, the incentive amount, and the incentive program code that identifies a merchant loyalty or incentive program. store_info. The store information. Includes the ID of the merchant store and the terminal ID for the checkout stand in the merchant store.</param>
    /// <param name="balanceAffectingRecordsOnly">Indicates whether the response includes only balance-impacting transactions or all transactions. Value is either: Y. The default. The response includes only balance transactions. N. The response includes all transactions.</param>
    /// <param name="pageSize">The number of items to return in the response. So, the combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items. The combination of <c>page=2</c> and <c>page_size=20</c> returns the next 20 items.</param>
    /// <param name="page">The zero-relative start index of the entire list of items that are returned in the response. So, the combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SearchResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists transactions. Specify one or more query parameters to filter the transaction that appear in the response. Notes: If you specify one or more optional query parameters, the ending_balance response field is empty. It takes a maximum of three hours for executed transactions to appear in the list transactions call. This call lists transaction for the previous three years.
    /// </remarks>
    public Task<SearchResponse> SearchTransactions(string startDate,
        string endDate,
        string? transactionId,
        string? transactionType,
        string? transactionStatus,
        string? transactionAmount,
        string? transactionCurrency,
        string? paymentInstrumentType,
        string? storeId,
        string? terminalId,
        string? fields = "transaction_info",
        string? balanceAffectingRecordsOnly = "Y",
        int? pageSize = 100,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/reporting/transactions"),
            [],
            [new Param("start_date", startDate),
                new Param("end_date", endDate),
                new Param("transaction_id", transactionId),
                new Param("transaction_type", transactionType),
                new Param("transaction_status", transactionStatus),
                new Param("transaction_amount", transactionAmount),
                new Param("transaction_currency", transactionCurrency),
                new Param("payment_instrument_type", paymentInstrumentType),
                new Param("store_id", storeId),
                new Param("terminal_id", terminalId),
                new Param("fields", fields),
                new Param("balance_affecting_records_only", balanceAffectingRecordsOnly),
                new Param("page_size", pageSize),
                new Param("page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SearchResponse>(),
            RawErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);
}
