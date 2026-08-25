using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to,
                   IReadRepository<Order> orderRepo, IPayPalService paypal, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), orderRepo, paypal, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReadRepository<Order> orderRepo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(ReconciliationQuery query, IReadRepository<Order> orderRepo,
        IPayPalService paypal, CancellationToken ct)
    {
        // Fetch PayPal transactions for the range
        IReadOnlyList<PayPalTransactionInfo> paypalTransactions;
        try
        {
            paypalTransactions = await paypal.SearchTransactionsAsync(query.From, query.To, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Transaction search failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }

        // Fetch all fulfilled/partially-refunded/refunded orders (have a capture)
        var allOrders = await orderRepo.ListAsync(ct);
        var paidOrders = allOrders
            .Where(o => o.Payment?.PayPalOrderId is not null)
            .ToList();

        // Index PayPal transactions by InvoiceId (= our orderId) and by TransactionId
        var transactionsByInvoice = paypalTransactions
            .Where(t => t.InvoiceId is not null)
            .ToLookup(t => t.InvoiceId!);

        var transactionIds = paypalTransactions.Select(t => t.TransactionId).ToHashSet();

        var records = new List<ReconciliationRecord>();

        // PayPal transactions — match to our orders
        foreach (var txn in paypalTransactions)
        {
            var matchedOrder = paidOrders
                .FirstOrDefault(o => o.Payment!.PayPalOrderId == txn.CustomField
                    || o.Id.ToString() == txn.InvoiceId
                    || txn.InvoiceId?.EndsWith($"-{o.Id}") == true);

            records.Add(new ReconciliationRecord
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalAmount = txn.Amount,
                PayPalFee = txn.Fee,
                PayPalStatus = txn.Status,
                InvoiceId = txn.InvoiceId,
                OrderId = matchedOrder?.Id,
                OrderStatus = matchedOrder?.Status.ToString(),
                OrderTotal = matchedOrder?.Total(),
                Matched = matchedOrder is not null
            });
        }

        // Our orders with no matching PayPal transaction
        foreach (var order in paidOrders)
        {
            var hasMatch = transactionsByInvoice[order.Id.ToString()].Any()
                || paypalTransactions.Any(t => t.InvoiceId?.EndsWith($"-{order.Id}") == true);

            if (!hasMatch)
            {
                records.Add(new ReconciliationRecord
                {
                    PayPalTransactionId = null,
                    OrderId = order.Id,
                    OrderStatus = order.Status.ToString(),
                    OrderTotal = order.Total(),
                    InvoiceId = order.Id.ToString(),
                    Matched = false,
                    Note = "Order exists in eShop but no matching PayPal transaction found in range (may be outside date range or reporting lag)"
                });
            }
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = query.From,
            To = query.To,
            Records = records,
            TotalPayPalTransactions = paypalTransactions.Count,
            UnmatchedPayPalCount = records.Count(r => r.PayPalTransactionId is not null && !r.Matched),
            UnmatchedOrderCount = records.Count(r => r.OrderId is not null && r.PayPalTransactionId is null)
        });
    }
}
