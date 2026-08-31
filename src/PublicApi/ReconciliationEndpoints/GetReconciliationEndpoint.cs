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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: PayPal's own record of transactions over a date range, lined up against local
/// orders/payments. Covers the whole range, not just the first page. Note PayPal's reporting lags
/// live activity, so a range covering just-created payments may legitimately be empty.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, reconciliationService, ct);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService, CancellationToken ct)
    {
        if (from == default || to == default)
        {
            return Results.BadRequest(new { message = "from and to are required ISO-8601 date-times." });
        }
        if (to < from)
        {
            return Results.BadRequest(new { message = "to must not be earlier than from." });
        }

        var report = await reconciliationService.GetReportAsync(from, to, ct);

        var response = new GetReconciliationResponse
        {
            From = report.From,
            To = report.To
        };
        response.Transactions.AddRange(report.Transactions.Select(t => new ReconciliationTransactionDto
        {
            TransactionId = t.TransactionId,
            PayPalReferenceId = t.PayPalReferenceId,
            ReferenceIdType = t.ReferenceIdType,
            InvoiceId = t.InvoiceId,
            CustomField = t.CustomField,
            EventCode = t.EventCode,
            Time = t.Time,
            Amount = t.Amount,
            Currency = t.Currency,
            Fee = t.Fee,
            Status = t.Status,
            MatchedOrderId = t.MatchedOrderId,
            MatchedPaymentId = t.MatchedPaymentId,
            MatchState = t.MatchState
        }));
        response.UnmatchedLocalPayments.AddRange(report.UnmatchedLocalPayments.Select(p => new UnmatchedLocalPaymentDto
        {
            PaymentId = p.PaymentId,
            OrderId = p.OrderId,
            BuyerId = p.BuyerId,
            Amount = p.Amount,
            Currency = p.Currency,
            Status = p.Status,
            PayPalOrderId = p.PayPalOrderId,
            AuthorizationId = p.AuthorizationId,
            CaptureId = p.CaptureId,
            CreatedAt = p.CreatedAt,
            MatchState = p.MatchState
        }));

        return Results.Ok(response);
    }
}
