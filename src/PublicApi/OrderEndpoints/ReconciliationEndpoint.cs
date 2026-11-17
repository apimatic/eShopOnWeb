using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: compares Finix transactions for a date range with eShop orders.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var report = await paymentService.GetReconciliationAsync(from, to, cancellationToken);
                var response = new ReconciliationResponse(Guid.NewGuid())
                {
                    From = report.From,
                    To = report.To,
                    FinixTransactions = report.FinixTransactions.Select(ToDto).ToList(),
                    UnmatchedFinixTransactions = report.UnmatchedFinixTransactions.Select(ToDto).ToList(),
                    EshopOrdersWithoutFinixRecord = report.EshopOrdersWithoutFinixRecord.Select(e => new EshopPaymentDto
                    {
                        OrderId = e.OrderId,
                        PaymentId = e.PaymentId,
                        PaymentStatus = e.PaymentStatus,
                        AuthorizationId = e.AuthorizationId,
                        CaptureTransferId = e.CaptureTransferId,
                        Amount = e.AmountCents.HasValue ? e.AmountCents.Value / 100m : null,
                        CapturedAmount = e.CapturedAmountCents.HasValue ? e.CapturedAmountCents.Value / 100m : null,
                        Currency = e.Currency,
                        AuthorizedAt = e.AuthorizedAt,
                        CapturedAt = e.CapturedAt
                    }).ToList()
                };

                response.Summary = new ReconciliationSummaryDto
                {
                    FinixTransactionCount = response.FinixTransactions.Count,
                    MatchedCount = response.FinixTransactions.Count(t => t.Matched),
                    UnmatchedFinixCount = response.UnmatchedFinixTransactions.Count,
                    EshopOnlyCount = response.EshopOrdersWithoutFinixRecord.Count
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    private static ReconciliationTransactionDto ToDto(ReconciliationEntry entry)
    {
        return new ReconciliationTransactionDto
        {
            FinixId = entry.FinixId,
            Kind = entry.Kind,
            Type = entry.Type,
            State = entry.State,
            Amount = entry.AmountCents / 100m,
            Currency = entry.Currency,
            CreatedAt = entry.CreatedAt,
            OrderId = entry.OrderId,
            Matched = entry.Matched,
            MatchType = entry.MatchType
        };
    }
}
