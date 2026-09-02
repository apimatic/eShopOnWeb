using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders/payments, so discrepancies in either direction are visible.
/// Covers the whole range (all PayPal result pages).
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private const int MaxRangeDays = 31; // PayPal Transaction Search limit

    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<Payment> _paymentRepository;

    public GetReconciliationEndpoint(IPayPalClient payPalClient, IRepository<Payment> paymentRepository)
    {
        _payPalClient = payPalClient;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (from == default || to == default)
        {
            return Results.BadRequest("Both 'from' and 'to' query parameters are required (ISO-8601 date-times).");
        }
        if (to <= from)
        {
            return Results.BadRequest("'to' must be after 'from'.");
        }
        if (to - from > TimeSpan.FromDays(MaxRangeDays))
        {
            return Results.BadRequest($"The date range cannot exceed {MaxRangeDays} days (PayPal Transaction Search limit).");
        }

        var transactions = await _payPalClient.ListTransactionsAsync(from, to);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpecification());

        // Index every PayPal-owned id eShop knows about.
        var eShopIds = new Dictionary<string, Payment>();
        foreach (var payment in payments)
        {
            if (payment.PayPalOrderId != null) eShopIds[payment.PayPalOrderId] = payment;
            if (payment.AuthorizationId != null) eShopIds[payment.AuthorizationId] = payment;
            if (payment.CaptureId != null) eShopIds[payment.CaptureId] = payment;
            foreach (var refund in payment.Refunds)
            {
                eShopIds[refund.PayPalRefundId] = payment;
            }
        }

        var entries = new List<ReconciliationEntry>();
        var seenPayPalIds = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            seenPayPalIds.Add(transaction.TransactionId);
            var matched = eShopIds.TryGetValue(transaction.TransactionId, out var payment);
            if (!matched && transaction.ReferenceId != null)
            {
                matched = eShopIds.TryGetValue(transaction.ReferenceId, out payment);
            }

            entries.Add(new ReconciliationEntry
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                TransactionStatus = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                InitiatedAt = transaction.InitiatedAt,
                MatchedOrderId = matched ? payment!.OrderId : null,
                MatchedPaymentId = matched ? payment!.Id : null,
                Match = matched ? "Matched" : "OnlyInPayPal"
            });
        }

        // eShop payments with activity inside the range that PayPal's report does not mention.
        foreach (var payment in payments)
        {
            var inRange = (payment.AuthorizedAt.HasValue && payment.AuthorizedAt >= from && payment.AuthorizedAt <= to)
                || (payment.CapturedAt.HasValue && payment.CapturedAt >= from && payment.CapturedAt <= to);
            if (!inRange)
            {
                continue;
            }

            var knownIds = new[] { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId }
                .Concat(payment.Refunds.Select(r => r.PayPalRefundId))
                .Where(id => id != null)
                .Cast<string>();

            if (!knownIds.Any(seenPayPalIds.Contains))
            {
                entries.Add(new ReconciliationEntry
                {
                    TransactionId = payment.CaptureId ?? payment.AuthorizationId ?? payment.PayPalOrderId,
                    TransactionStatus = payment.Status.ToString(),
                    Amount = payment.CapturedAmount ?? payment.Amount,
                    Currency = payment.Currency,
                    InitiatedAt = payment.CapturedAt ?? payment.AuthorizedAt,
                    MatchedOrderId = payment.OrderId,
                    MatchedPaymentId = payment.Id,
                    Match = "OnlyInEShop"
                });
            }
        }

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            TotalPayPalTransactions = transactions.Count,
            MatchedCount = entries.Count(e => e.Match == "Matched"),
            OnlyInPayPalCount = entries.Count(e => e.Match == "OnlyInPayPal"),
            OnlyInEShopCount = entries.Count(e => e.Match == "OnlyInEShop"),
            Entries = entries
        };
        return Results.Ok(response);
    }
}

public class ReconciliationEntry
{
    public string? TransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
    /// <summary>Matched | OnlyInPayPal | OnlyInEShop</summary>
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInPayPalCount { get; set; }
    public int OnlyInEShopCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
}
