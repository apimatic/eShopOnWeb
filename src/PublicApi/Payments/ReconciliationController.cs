using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public sealed class ReconciliationEntry
{
    public string MatchStatus { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? LocalTransactionType { get; set; }
    public string? LocalTransactionId { get; set; }
    public string? LocalStatus { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? PayPalEventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? TransactionTime { get; set; }
}

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;

    public ReconciliationController(CatalogContext db, IPayPalClient payPal)
    {
        _db = db;
        _payPal = payPal;
    }

    [HttpGet("api/reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Get([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from >= to)
            throw new PaymentWorkflowException(HttpStatusCode.BadRequest,
                "from and to must be valid ISO-8601 date-times and from must precede to.");

        var payPalTransactions = await ReadEveryPayPalPage(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.PayPalCaptureId != null || x.Refunds.Any())
            .ToListAsync(cancellationToken);
        var localTransactions = BuildLocalTransactions(orders, from, to);
        var localById = localTransactions.Where(x => x.TransactionId is not null)
            .ToDictionary(x => x.TransactionId!, StringComparer.Ordinal);
        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var transaction in payPalTransactions.OrderBy(x => x.InitiatedAt))
        {
            localById.TryGetValue(transaction.Id, out var local);
            if (local is null && transaction.ReferenceId is not null)
                localById.TryGetValue(transaction.ReferenceId, out local);
            if (local is not null && local.TransactionId is not null) matchedLocalIds.Add(local.TransactionId);
            entries.Add(new ReconciliationEntry
            {
                MatchStatus = local is null ? "PayPalOnly" : "Matched",
                OrderId = local?.OrderId ?? ParseOrderId(transaction.InvoiceId, transaction.CustomId),
                LocalTransactionType = local?.Type,
                LocalTransactionId = local?.TransactionId,
                LocalStatus = local?.Status,
                PayPalTransactionId = transaction.Id,
                PayPalReferenceId = transaction.ReferenceId,
                PayPalEventCode = transaction.EventCode,
                PayPalStatus = transaction.Status,
                Amount = transaction.Amount,
                Fee = transaction.Fee,
                Currency = transaction.Currency,
                TransactionTime = transaction.InitiatedAt
            });
        }

        entries.AddRange(localTransactions.Where(x => x.TransactionId is not null &&
            !matchedLocalIds.Contains(x.TransactionId)).Select(local => new ReconciliationEntry
            {
                MatchStatus = "EShopOnly",
                OrderId = local.OrderId,
                LocalTransactionType = local.Type,
                LocalTransactionId = local.TransactionId,
                LocalStatus = local.Status,
                Amount = local.Amount,
                Fee = local.Fee,
                Currency = local.Currency,
                TransactionTime = local.Time
            }));

        return Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            PayPalTransactionCount = payPalTransactions.Count,
            Entries = entries.OrderBy(x => x.TransactionTime).ToList()
        });
    }

    private async Task<List<PayPalTransaction>> ReadEveryPayPalPage(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        var all = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31) < to ? chunkStart.AddDays(31) : to;
            var page = 1;
            while (true)
            {
                var result = await _payPal.SearchTransactionsAsync(chunkStart, chunkEnd, page,
                    pageSize, cancellationToken);
                foreach (var item in result.Transactions)
                {
                    var key = string.Create(CultureInfo.InvariantCulture,
                        $"{item.Id}|{item.EventCode}|{item.UpdatedAt:O}");
                    all[key] = item;
                }
                if (page >= result.TotalPages) break;
                page++;
            }
            chunkStart = chunkEnd;
        }
        return all.Values.ToList();
    }

    private static List<LocalTransaction> BuildLocalTransactions(IEnumerable<Order> orders,
        DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<LocalTransaction>();
        foreach (var order in orders)
        {
            if (order.PayPalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
                result.Add(new LocalTransaction(order.Id, "Capture", order.PayPalCaptureId,
                    order.PayPalCaptureStatus, order.CapturedAmount, order.PayPalFee,
                    order.PaymentCurrency, order.CapturedAt));
            result.AddRange(order.Refunds.Where(x => x.PayPalRefundId is not null &&
                    (x.UpdatedAt ?? x.CreatedAt) >= from && (x.UpdatedAt ?? x.CreatedAt) <= to)
                .Select(x => new LocalTransaction(order.Id, "Refund", x.PayPalRefundId,
                    x.Status, -x.Amount, x.RefundedPayPalFee, order.PaymentCurrency,
                    x.UpdatedAt ?? x.CreatedAt)));
        }
        return result;
    }

    private static int? ParseOrderId(string? invoiceId, string? customId)
    {
        if (invoiceId?.StartsWith("ESHOP-", StringComparison.Ordinal) == true &&
            int.TryParse(invoiceId[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var invoiceOrderId))
            return invoiceOrderId;
        return int.TryParse(customId, NumberStyles.None, CultureInfo.InvariantCulture, out var orderId)
            ? orderId : null;
    }

    private sealed record LocalTransaction(int OrderId, string Type, string? TransactionId,
        string? Status, decimal? Amount, decimal? Fee, string? Currency, DateTimeOffset? Time);
}
