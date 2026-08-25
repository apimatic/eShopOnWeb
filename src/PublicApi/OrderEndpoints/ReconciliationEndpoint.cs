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
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReadRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;

    public ReconciliationEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReadRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var request = new ReconciliationRequest { From = from, To = to };
                return await HandleAsync(request, orderRepo);
            })
            .Produces<ReconciliationResponse>()
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReadRepository<Order> orderRepo)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
            return Results.BadRequest("Invalid 'from' date-time.");
        if (!DateTimeOffset.TryParse(request.To, out var to))
            return Results.BadRequest("Invalid 'to' date-time.");

        List<PayPalTransaction> payPalTxns;
        try
        {
            payPalTxns = (await _payPal.SearchTransactionsAsync(from, to)).ToList();
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "PayPal reconciliation error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }

        var allOrders = await orderRepo.ListAsync();
        var ordersByCapture = allOrders
            .Where(o => o.CaptureId != null)
            .ToDictionary(o => o.CaptureId!, o => o);
        var ordersByPayPalOrderId = allOrders
            .Where(o => o.PayPalOrderId != null)
            .ToDictionary(o => o.PayPalOrderId!, o => o);
        // Also index by eShop orderId string for InvoiceId matching
        var ordersByEShopId = allOrders.ToDictionary(o => o.Id.ToString(), o => o);

        var items = new List<ReconciliationItem>();

        // PayPal-first: match txns to orders.
        // Matching strategy (in priority order):
        //   1. txn.InvoiceId == eShop orderId (set on CreateOrder)
        //   2. txn.TransactionId == CaptureId
        //   3. txn.ReferenceId == CaptureId or PayPalOrderId
        var matchedOrderIds = new HashSet<int>();
        foreach (var txn in payPalTxns)
        {
            Order? matched = null;
            // Match by InvoiceId (format: "{runPrefix}-inv-{orderId}" or bare orderId for older records)
            if (txn.InvoiceId != null)
            {
                // Try exact match first (legacy bare-orderId format)
                if (!ordersByEShopId.TryGetValue(txn.InvoiceId, out matched))
                {
                    // Extract orderId from prefixed format: last segment after the last "-inv-"
                    var sep = "-inv-";
                    var idx = txn.InvoiceId.LastIndexOf(sep, StringComparison.Ordinal);
                    if (idx >= 0)
                        ordersByEShopId.TryGetValue(txn.InvoiceId.Substring(idx + sep.Length), out matched);
                }
            }
            // Match by capture ID
            if (matched == null && txn.TransactionId != null)
                ordersByCapture.TryGetValue(txn.TransactionId, out matched);
            // Fall back to matching by PayPal order ID via ReferenceId
            if (matched == null && txn.ReferenceId != null)
            {
                ordersByCapture.TryGetValue(txn.ReferenceId, out matched);
                if (matched == null)
                    ordersByPayPalOrderId.TryGetValue(txn.ReferenceId, out matched);
            }

            if (matched != null)
                matchedOrderIds.Add(matched.Id);

            items.Add(new ReconciliationItem
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.ReferenceId,
                PayPalAmount = txn.Amount,
                PayPalStatus = txn.Status,
                PayPalInitiatedAt = txn.InitiatedAt,
                eShopOrderId = matched?.Id,
                eShopOrderStatus = matched?.PaymentStatus.ToString(),
                Match = matched != null ? "matched" : "paypal-only"
            });
        }

        // eShop orders with PayPal IDs not seen in PayPal report
        foreach (var order in allOrders
            .Where(o => o.PayPalOrderId != null && !matchedOrderIds.Contains(o.Id)))
        {
            if (order.OrderDate < from || order.OrderDate > to) continue;

            items.Add(new ReconciliationItem
            {
                eShopOrderId = order.Id,
                eShopOrderStatus = order.PaymentStatus.ToString(),
                Match = "eshop-only"
            });
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            Items = items
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationItem> Items { get; set; } = new();
}

public class ReconciliationItem
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public DateTimeOffset? PayPalInitiatedAt { get; set; }
    public int? eShopOrderId { get; set; }
    public string? eShopOrderStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}
