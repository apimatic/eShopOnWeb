using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements reconciliation reads against the PayPal Transaction Search v1 API. The spec limits a
/// query to a 31-day window and pages results, so this gateway splits [from, to] into windows and
/// pages through each so the caller gets the WHOLE range, not just the first page.
/// </summary>
public class PayPalReportingGateway : IPayPalReportingGateway
{
    // Spec: "The maximum supported range is 31 days." Stay just under to be safe.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31) - TimeSpan.FromMinutes(1);
    private const int PageSize = 500; // spec max

    private readonly PayPalApiClient _client;
    private readonly ILogger<PayPalReportingGateway> _logger;

    public PayPalReportingGateway(PayPalApiClient client, ILogger<PayPalReportingGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReportedTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));
        }

        var results = new List<ReportedTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxWindow;
            if (windowEnd > to) windowEnd = to;

            await FetchWindowAsync(windowStart, windowEnd, results, seen, cancellationToken);

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task FetchWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<ReportedTransaction> results,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var page = 1;
        while (true)
        {
            SearchResponse? response;
            try
            {
                response = await FetchPageAsync(from, to, page, cancellationToken);
            }
            catch (PaymentGatewayException ex) when (IsResultSetTooLarge(ex) && (to - from) > TimeSpan.FromDays(1))
            {
                // Narrow the window and recurse: split in half.
                var mid = from + TimeSpan.FromTicks((to - from).Ticks / 2);
                _logger.LogWarning("PayPal reporting window too large; splitting {From}..{To} at {Mid}.", from, to, mid);
                await FetchWindowAsync(from, mid, results, seen, cancellationToken);
                await FetchWindowAsync(mid, to, results, seen, cancellationToken);
                return;
            }

            if (response?.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;

                    // The same transaction can appear across overlapping window boundaries; de-duplicate.
                    if (!seen.Add(info.TransactionId)) continue;

                    results.Add(new ReportedTransaction(
                        info.TransactionId,
                        info.TransactionStatus ?? "?",
                        PayPalMoney.Parse(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode ?? string.Empty,
                        ParseDate(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField,
                        info.PayPalReferenceId));
                }
            }

            var totalPages = response?.TotalPages ?? 0;
            if (page >= totalPages)
            {
                break;
            }
            page++;
        }
    }

    private async Task<SearchResponse?> FetchPageAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken)
    {
        var startDate = Uri.EscapeDataString(FormatRfc3339(from));
        var endDate = Uri.EscapeDataString(FormatRfc3339(to));
        var path =
            $"/v1/reporting/transactions?start_date={startDate}&end_date={endDate}" +
            $"&fields=transaction_info&page_size={PageSize}&page={page}";

        return await _client.SendAsync<SearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
    }

    private static bool IsResultSetTooLarge(PaymentGatewayException ex) =>
        string.Equals(ex.ErrorName, "RESULTSET_TOO_LARGE", StringComparison.OrdinalIgnoreCase)
        || ex.Issue == "RESULTSET_TOO_LARGE";

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
