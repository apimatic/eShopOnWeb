using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range
/// and lines them up against eShop orders, so a payment only one side knows
/// about is visible. Covers the whole range, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    // PayPal's Transaction Search API supports a maximum range of 31 days.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPayPalGateway gateway, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(from, to, gateway, paymentRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to,
        IPayPalGateway gateway, IRepository<Payment> paymentRepository)
    {
        if (to <= from)
        {
            return Results.BadRequest("The 'to' date-time must be after the 'from' date-time.");
        }
        if (to - from > MaxRange)
        {
            return Results.BadRequest("The date range must not exceed 31 days (PayPal Transaction Search limit).");
        }

        var transactions = await gateway.ListTransactionsAsync(from, to);
        var payments = await paymentRepository.ListAsync(new PaymentsWithRefundsSpecification());

        var response = new ReconciliationResponse { From = from, To = to };

        foreach (var transaction in transactions)
        {
            var match = payments.FirstOrDefault(p =>
                p.PayPalOrderId == transaction.TransactionId ||
                p.AuthorizationId == transaction.TransactionId ||
                p.CaptureId == transaction.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));

            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Time = transaction.Time,
                Match = match is null ? "paypalOnly" : "matched",
                OrderId = match?.OrderId
            });
        }

        var knownIds = transactions.Select(t => t.TransactionId).ToHashSet();
        response.UnmatchedEshopPayments = payments
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Where(p => (p.AuthorizationId is not null && !knownIds.Contains(p.AuthorizationId)) ||
                        (p.CaptureId is not null && !knownIds.Contains(p.CaptureId)) ||
                        p.Refunds.Any(r => !knownIds.Contains(r.PayPalRefundId)))
            .Select(p => new ReconciliationLocalPaymentDto
            {
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                CapturedAmount = p.CapturedAmount,
                Currency = p.Currency
            })
            .ToList();

        return Results.Ok(response);
    }
}
