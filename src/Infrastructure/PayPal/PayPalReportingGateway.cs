using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Reads PayPal's own transaction record for reconciliation via Transaction Search v1
/// (<c>GET /v1/reporting/transactions</c>). PayPal caps each query at a 31-day window and paginates
/// results, so this pages through every page of every 31-day window across the requested range,
/// covering the whole range rather than just its first page.
/// </summary>
public class PayPalReportingGateway : IPayPalReportingGateway
{
    private readonly PayPalApiClient _client;

    // PayPal limits transaction search to a 31-day window per request; stay a little under that.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);
    private const int PageSize = 500;

    public PayPalReportingGateway(PayPalApiClient client) => _client = client;

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var results = new List<ReconciliationTransaction>();
        if (endDate < startDate)
        {
            return results;
        }

        var windowStart = startDate;
        while (windowStart < endDate)
        {
            var windowEnd = windowStart + MaxWindow;
            if (windowEnd > endDate)
            {
                windowEnd = endDate;
            }

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);

            // Advance a tick past the window end so consecutive windows don't overlap on the boundary.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task CollectWindowAsync(DateTimeOffset from, DateTimeOffset to,
        List<ReconciliationTransaction> results, CancellationToken cancellationToken)
    {
        var page = 1;
        while (true)
        {
            var path = "/v1/reporting/transactions" +
                       $"?start_date={Encode(from)}&end_date={Encode(to)}" +
                       $"&fields=transaction_info&page_size={PageSize}&page={page}";

            var response = await _client.SendAsync<SearchResponse>(HttpMethod.Get, path, body: null,
                headers: null, cancellationToken);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null) continue;
                    results.Add(Map(info));
                }
            }

            var totalPages = response.TotalPages ?? 1;
            if (page >= totalPages)
            {
                break;
            }
            page++;
        }
    }

    private static ReconciliationTransaction Map(TransactionInfoModel info)
    {
        decimal? amount = null;
        string? currency = null;
        if (info.TransactionAmount?.Value != null &&
            decimal.TryParse(info.TransactionAmount.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
        {
            amount = v;
            currency = info.TransactionAmount.CurrencyCode;
        }

        DateTimeOffset? initiated = DateTimeOffset.TryParse(info.TransactionInitiationDate,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : null;

        return new ReconciliationTransaction(
            info.TransactionId ?? string.Empty,
            info.TransactionStatus,
            info.TransactionEventCode,
            initiated,
            amount,
            currency,
            info.FeeAmount?.Value,
            info.InvoiceId,
            info.CustomField,
            info.PayPalReferenceId,
            info.PaymentMethodType ?? info.InstrumentType);
    }

    // PayPal transaction search expects an RFC 3339 date-time; use UTC with an explicit offset.
    private static string Encode(DateTimeOffset value)
        => WebUtility.UrlEncode(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
}
