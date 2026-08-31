using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// ITransactionSearch over the PayPal Transaction Search API. Walks every page of the requested
/// range. Note PayPal's reporting lags live activity (up to a few hours in sandbox), so a range
/// covering just-created payments may legitimately come back empty.
/// </summary>
public class PayPalTransactionSearch : ITransactionSearch
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalTransactionSearch> _logger;

    public PayPalTransactionSearch(PayPalServerSdkClient client, ILogger<PayPalTransactionSearch> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;
        do
        {
            var response = await SearchPageAsync(from, to, page, ct);
            if (response.TransactionDetails != null)
            {
                transactions.AddRange(response.TransactionDetails.Select(Map));
            }
            totalPages = response.TotalPages ?? page;
            page++;
        }
        while (page <= totalPages && !ct.IsCancellationRequested);

        return transactions;
    }

    private async Task<PayPalServerSdk.Models.SearchResponse> SearchPageAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await _client.TransactionSearch.SearchTransactions(
                // PayPal rejects round-trip ("O") precision; milliseconds are the accepted maximum.
                startDate: from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
                endDate: to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
                transactionId: null,
                transactionType: null,
                transactionStatus: null,
                transactionAmount: null,
                transactionCurrency: null,
                paymentInstrumentType: null,
                storeId: null,
                terminalId: null,
                fields: "transaction_info",
                balanceAffectingRecordsOnly: null,
                pageSize: PageSize,
                page: page,
                ct: cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            // Transaction search is the SDK's one operation without a typed error model.
            var status = (int)ex.Error.StatusCode;
            string detail;
            try
            {
                detail = ex.Error.ReadAsString();
            }
            catch
            {
                detail = string.Empty;
            }
            if (detail.Length > 300)
            {
                detail = detail[..300];
            }
            _logger.LogWarning("PayPal transaction search failed with HTTP {Status}: {Detail}", status, detail);
            throw new PaymentGatewayException($"PayPal transaction search failed (HTTP {status}).", status, null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new PaymentGatewayException("The payment provider did not respond within the allowed time.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("The payment provider could not be reached.", null, null, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentGatewayException("The payment provider returned a response that could not be processed.",
                PayPalResponseStatusTracker.LastStatus, null, ex);
        }
    }

    private static GatewayTransaction Map(PayPalServerSdk.Models.TransactionDetails details)
    {
        var info = details.TransactionInfo;
        return new GatewayTransaction(
            info?.TransactionId ?? string.Empty,
            info?.PaypalReferenceId,
            info?.PaypalReferenceIdType.WireValue(),
            info?.InvoiceId,
            info?.CustomField,
            info?.TransactionEventCode,
            PayPalPaymentGateway.ParseDate(info?.TransactionInitiationDate),
            PayPalPaymentGateway.ParseMoney(info?.TransactionAmount),
            info?.TransactionAmount?.CurrencyCode,
            PayPalPaymentGateway.ParseMoney(info?.FeeAmount),
            info?.TransactionStatus);
    }
}
