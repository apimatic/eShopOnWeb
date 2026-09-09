using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — an operator report listing PayPal's own record of
/// transactions over the whole date range (all pages) lined up against eShop orders, so a payment PayPal
/// knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Args, IPaymentGateway, IReadRepository<OrderPayment>>
{
    public record Args(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentGateway gateway, IReadRepository<OrderPayment> payments) =>
                await HandleAsync(new Args(from, to), gateway, payments))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, IPaymentGateway gateway, IReadRepository<OrderPayment> paymentRepository)
    {
        if (args.To < args.From)
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

        var transactions = await gateway.SearchTransactionsAsync(args.From, args.To);
        var payments = await paymentRepository.ListAsync();
        var byOrderId = new Dictionary<int, OrderPayment>();
        foreach (var p in payments)
            byOrderId[p.OrderId] = p;

        var response = new ReconciliationResponse
        {
            From = args.From,
            To = args.To,
            PayPalTransactionCount = transactions.Count,
            EShopOrderCount = payments.Count
        };

        var matchedOrderIds = new HashSet<int>();

        // Every transaction PayPal reports, matched to an eShop order by the reference we stamped on it.
        foreach (var txn in transactions)
        {
            OrderPayment? matched = null;
            int? matchedOrderId = null;
            if (int.TryParse(txn.OrderReference, out var oid) && byOrderId.TryGetValue(oid, out matched))
            {
                matchedOrderId = oid;
                matchedOrderIds.Add(oid);
            }

            response.Lines.Add(new ReconciliationLineDto(
                txn.TransactionId,
                txn.OrderReference,
                matchedOrderId,
                txn.Amount,
                txn.CurrencyCode,
                txn.FeeAmount,
                txn.Status,
                txn.Date,
                matched?.CapturedAmount ?? matched?.Amount,
                matched?.Status.ToString(),
                matched is null ? "PayPalOnly" : "Matched"));
        }

        // eShop orders where money moved but no PayPal transaction appeared in the range (reporting lag,
        // or a genuine discrepancy) — surfaced so the reverse gap is visible too.
        foreach (var p in payments)
        {
            if (matchedOrderIds.Contains(p.OrderId))
                continue;
            if (p.Status is not (PaymentStatus.Fulfilled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded))
                continue;

            response.Lines.Add(new ReconciliationLineDto(
                null, p.OrderId.ToString(), p.OrderId,
                null, p.CurrencyCode, null, null, null,
                p.CapturedAmount ?? p.Amount, p.Status.ToString(), "EShopOnly"));
        }

        return Results.Ok(response);
    }
}
