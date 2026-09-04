using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up
/// against eShop orders, so a payment one side knows about and the other doesn't is
/// visible. Covers the whole range (all pages), not just its first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationResponse, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationResponse(Guid.NewGuid()), orderPaymentService, http, from, to, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationResponse request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, DateTimeOffset.MinValue, DateTimeOffset.MinValue, CancellationToken.None);

    public async Task<IResult> HandleAsync(ReconciliationResponse request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from == default || to == default)
        {
            return Results.BadRequest(new { message = "Both from and to (ISO-8601 date-times) are required." });
        }
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await orderPaymentService.ReconcileAsync(from, to, ct);

        request.From = report.From;
        request.To = report.To;
        request.TotalTransactions = report.TotalTransactions;
        request.Transactions = report.Transactions.Select(t => new ReconciliationTransactionDto
        {
            TransactionId = t.TransactionId,
            Status = t.Status,
            Amount = t.Amount.Amount,
            Currency = t.Amount.Currency,
            InitiationDate = t.InitiationDate,
            OrderId = t.OrderId,
            OrderStatus = t.OrderStatus
        }).ToList();
        request.UnmatchedOrders = report.UnmatchedOrders.Select(o => new ReconciliationUnmatchedOrderDto
        {
            OrderId = o.OrderId!.Value,
            Status = o.OrderStatus ?? string.Empty,
            Amount = o.Amount.Amount,
            Currency = o.Amount.Currency,
            PaymentDate = o.InitiationDate
        }).ToList();
        return Results.Ok(request);
    }
}
