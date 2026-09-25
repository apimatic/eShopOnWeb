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
using PayPalServerSdk.Requests.TransactionSearch;

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
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BalancesResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="SearchBalancesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all balances. Specify date time to list balances for that time that appear in the response. Notes: It takes a maximum of three hours for balances to appear in the list balances call. This call lists balances upto the previous three years.
    /// </remarks>
    public Task<BalancesResponse> SearchBalances(SearchBalancesRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/reporting/balances"),
            [],
            [new Param("as_of_time", request.AsOfTime), new Param("currency_code", request.CurrencyCode)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BalancesResponse>(),
            SearchBalancesError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// List transactions
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SearchResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists transactions. Specify one or more query parameters to filter the transaction that appear in the response. Notes: If you specify one or more optional query parameters, the ending_balance response field is empty. It takes a maximum of three hours for executed transactions to appear in the list transactions call. This call lists transaction for the previous three years.
    /// </remarks>
    public Task<SearchResponse> SearchTransactions(SearchTransactionsRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/reporting/transactions"),
            [],
            [new Param("start_date", request.StartDate),
                new Param("end_date", request.EndDate),
                new Param("transaction_id", request.TransactionId),
                new Param("transaction_type", request.TransactionType),
                new Param("transaction_status", request.TransactionStatus),
                new Param("transaction_amount", request.TransactionAmount),
                new Param("transaction_currency", request.TransactionCurrency),
                new Param("payment_instrument_type", request.PaymentInstrumentType),
                new Param("store_id", request.StoreId),
                new Param("terminal_id", request.TerminalId),
                new Param("fields", request.Fields),
                new Param("balance_affecting_records_only", request.BalanceAffectingRecordsOnly),
                new Param("page_size", request.PageSize),
                new Param("page", request.Page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SearchResponse>(),
            RawErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);
}
