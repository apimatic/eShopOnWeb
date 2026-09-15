using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record ReconciliationRequest(DateTimeOffset? From, DateTimeOffset? To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationSummary Summary { get; set; } = new();
    public List<ReconciliationTransaction> PayPalTransactions { get; set; } = new();
    public List<ReconciliationOrder> OrdersMissingFromPayPal { get; set; } = new();
}

public class ReconciliationSummary
{
    public int PayPalTransactionCount { get; set; }
    public int MatchedToOrder { get; set; }
    public int InPayPalNotInEShop { get; set; }
    public int EShopOrdersInRange { get; set; }
    public int InEShopNotInPayPal { get; set; }
}

public class ReconciliationTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? Date { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class ReconciliationOrder
{
    public int OrderId { get; set; }
    public string ReconciliationId { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? AuthorizationId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator report: PayPal's own record of transactions across a date range, lined up against eShop
/// orders. A payment PayPal knows about and eShop doesn't — or the reverse — is visible. Covers the whole
/// range (all pages, chunked by PayPal's 31-day cap), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPayPalPaymentService, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPayPalPaymentService payPal, IReadRepository<Order> orderRepository) =>
                await HandleAsync(new ReconciliationRequest(from, to), payPal, orderRepository))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPayPalPaymentService payPal,
        IReadRepository<Order> orderRepository)
    {
        if (request.From == null || request.To == null)
        {
            throw new PaymentException("Both 'from' and 'to' ISO-8601 date-times are required.");
        }

        var from = request.From.Value;
        var to = request.To.Value;

        var transactions = await payPal.SearchTransactionsAsync(from, to);
        var orders = await orderRepository.ListAsync(new PaidOrdersInDateRangeSpecification(from, to));

        // Index eShop orders by every id PayPal might echo back.
        var byReconId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var byTxnId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var p = order.Payment!;
            byReconId[p.ReconciliationId] = order;
            if (!string.IsNullOrEmpty(p.CaptureId)) byTxnId[p.CaptureId!] = order;
            if (!string.IsNullOrEmpty(p.AuthorizationId)) byTxnId[p.AuthorizationId!] = order;
        }

        var matchedOrderIds = new HashSet<int>();
        var txnDtos = new List<ReconciliationTransaction>();
        foreach (var t in transactions)
        {
            Order? matched = null;
            if (!string.IsNullOrEmpty(t.CustomField) && byReconId.TryGetValue(t.CustomField!, out var m1)) matched = m1;
            else if (!string.IsNullOrEmpty(t.InvoiceId) && byReconId.TryGetValue(t.InvoiceId!, out var m2)) matched = m2;
            else if (!string.IsNullOrEmpty(t.TransactionId) && byTxnId.TryGetValue(t.TransactionId, out var m3)) matched = m3;

            if (matched != null) matchedOrderIds.Add(matched.Id);

            txnDtos.Add(new ReconciliationTransaction
            {
                TransactionId = t.TransactionId,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                Date = t.InitiationDate,
                MatchedOrderId = matched?.Id
            });
        }

        var ordersMissing = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationOrder
            {
                OrderId = o.Id,
                ReconciliationId = o.Payment!.ReconciliationId,
                CaptureId = o.Payment!.CaptureId,
                AuthorizationId = o.Payment!.AuthorizationId,
                Amount = o.Payment!.Amount,
                Status = o.Status.ToString()
            })
            .ToList();

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            PayPalTransactions = txnDtos,
            OrdersMissingFromPayPal = ordersMissing,
            Summary = new ReconciliationSummary
            {
                PayPalTransactionCount = txnDtos.Count,
                MatchedToOrder = txnDtos.Count(t => t.MatchedOrderId != null),
                InPayPalNotInEShop = txnDtos.Count(t => t.MatchedOrderId == null),
                EShopOrdersInRange = orders.Count,
                InEShopNotInPayPal = ordersMissing.Count
            }
        };

        return Results.Ok(response);
    }
}
