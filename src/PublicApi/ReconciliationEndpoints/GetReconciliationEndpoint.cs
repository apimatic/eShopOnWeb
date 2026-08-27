using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions over a date range and lines
/// them up against eShop orders, so a payment only one side knows about is visible.
/// Covers the whole range (paged and chunked server-side), not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public GetReconciliationEndpoint(IPaymentGateway paymentGateway, IReadRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to) =>
        HandleAsync(from, to, CancellationToken.None);

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to)
        {
            return Results.BadRequest("'from' must be earlier than 'to'. Both are ISO-8601 date-times.");
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpecification(from, to), ct);

        var rows = transactions.Select(t => new ReconciliationTransactionDto
        {
            TransactionId = t.TransactionId,
            ReferenceId = t.ReferenceId,
            ReferenceIdType = t.ReferenceIdType,
            EventCode = t.EventCode,
            Status = t.Status,
            Amount = t.Amount,
            Currency = t.Currency,
            Fee = t.Fee,
            InitiatedAt = t.InitiatedAt,
            UpdatedAt = t.UpdatedAt,
            InvoiceId = t.InvoiceId,
            MatchedOrderId = FindMatchingOrder(t, orders)?.Id
        }).ToList();

        var matchedTransactionIds = new HashSet<string>(
            transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var matchedReferenceIds = new HashSet<string>(
            transactions.Select(t => t.ReferenceId).Where(r => r is not null)!, StringComparer.OrdinalIgnoreCase);

        var ordersMissingFromPayPal = orders
            .Where(o => o.PayPalOrderId is not null)
            .Where(o => !matchedReferenceIds.Contains(o.PayPalOrderId!)
                        && (o.AuthorizationId is null || !matchedTransactionIds.Contains(o.AuthorizationId))
                        && (o.CaptureId is null || !matchedTransactionIds.Contains(o.CaptureId)))
            .Select(o => new UnmatchedOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId
            })
            .ToList();

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            TransactionCount = rows.Count,
            Transactions = rows,
            UnmatchedPayPalTransactions = rows
                .Where(r => r.MatchedOrderId is null)
                .Select(r => r.TransactionId)
                .ToList(),
            OrdersMissingFromPayPal = ordersMissingFromPayPal
        });
    }

    private static Order? FindMatchingOrder(GatewayTransaction transaction, IReadOnlyList<Order> orders)
    {
        // PayPal references its own order id on related transactions (reference type ODR).
        if (transaction.ReferenceId is not null)
        {
            var byPayPalOrder = orders.FirstOrDefault(o => o.PayPalOrderId == transaction.ReferenceId);
            if (byPayPalOrder is not null) return byPayPalOrder;
        }

        return orders.FirstOrDefault(o =>
            o.AuthorizationId == transaction.TransactionId ||
            o.CaptureId == transaction.TransactionId ||
            o.Refunds.Any(r => r.RefundId == transaction.TransactionId));
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TransactionCount { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();

    /// <summary>PayPal transactions no eShop order could be lined up with.</summary>
    public List<string> UnmatchedPayPalTransactions { get; set; } = new();

    /// <summary>eShop orders with PayPal payment state that PayPal's report does not list.</summary>
    public List<UnmatchedOrderDto> OrdersMissingFromPayPal { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class UnmatchedOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}
