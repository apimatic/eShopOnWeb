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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lines up PayPal's own transaction record for a date range against eShop's
/// local order/payment records, so a payment either side knows about and the other doesn't is
/// visible. Walks every page PayPal returns for the range, not just the first.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest,
    (IRepository<OrderPayment> Payments, IRepository<Order> Orders, IPaymentGatewayService Gateway, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<OrderPayment> payments, IRepository<Order> orders,
             IPaymentGatewayService gateway, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, (payments, orders, gateway, ct));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request,
        (IRepository<OrderPayment> Payments, IRepository<Order> Orders, IPaymentGatewayService Gateway, CancellationToken Ct) dependency)
    {
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        var searchResult = await dependency.Gateway.SearchTransactionsAsync(request.From, request.To, dependency.Ct);
        var localPayments = await dependency.Payments.ListAsync(new OrderPaymentsInDateRangeSpec(request.From, request.To));

        var orderIds = localPayments.Select(p => p.OrderId).ToArray();
        var localOrders = await dependency.Orders.ListAsync(new OrdersByIdsSpec(orderIds));
        var orderStatusByOrderId = localOrders.ToDictionary(o => o.Id, o => o.Status.ToString());

        var matchedLocalIds = new HashSet<int>();
        var matchedTransactionIds = new HashSet<string>();
        var entries = new List<ReconciliationEntryDto>();

        foreach (var txn in searchResult.Transactions)
        {
            var match = localPayments.FirstOrDefault(p => Correlates(p, txn));
            if (match is null)
            {
                continue;
            }

            matchedLocalIds.Add(match.Id);
            matchedTransactionIds.Add(txn.TransactionId);
            orderStatusByOrderId.TryGetValue(match.OrderId, out var orderStatus);

            entries.Add(new ReconciliationEntryDto
            {
                MatchStatus = "Matched",
                PayPalTransactionId = txn.TransactionId,
                PayPalStatus = txn.Status,
                PayPalAmount = txn.Amount,
                PayPalCurrency = txn.CurrencyCode,
                OrderId = match.OrderId,
                OrderStatus = orderStatus,
                OrderCapturedAmount = match.CapturedAmount
            });
        }

        foreach (var txn in searchResult.Transactions.Where(t => !matchedTransactionIds.Contains(t.TransactionId)))
        {
            entries.Add(new ReconciliationEntryDto
            {
                MatchStatus = "PayPalOnly",
                PayPalTransactionId = txn.TransactionId,
                PayPalStatus = txn.Status,
                PayPalAmount = txn.Amount,
                PayPalCurrency = txn.CurrencyCode
            });
        }

        foreach (var payment in localPayments.Where(p => !matchedLocalIds.Contains(p.Id)))
        {
            orderStatusByOrderId.TryGetValue(payment.OrderId, out var orderStatus);
            entries.Add(new ReconciliationEntryDto
            {
                MatchStatus = "EShopOnly",
                OrderId = payment.OrderId,
                OrderStatus = orderStatus,
                OrderCapturedAmount = payment.CapturedAmount
            });
        }

        response.Entries = entries;
        return Results.Ok(response);
    }

    private static bool Correlates(OrderPayment payment, PayPalTransaction txn)
    {
        if (payment.CaptureId == txn.TransactionId || payment.AuthorizationId == txn.TransactionId || payment.PayPalOrderId == txn.TransactionId)
        {
            return true;
        }

        return txn.ReferenceId is not null &&
               (payment.CaptureId == txn.ReferenceId || payment.AuthorizationId == txn.ReferenceId || payment.PayPalOrderId == txn.ReferenceId);
    }
}
