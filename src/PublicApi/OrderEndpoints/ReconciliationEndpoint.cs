using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: compare Finix transactions with eShop orders for an ISO-8601 date-time range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, PaymentService paymentService) =>
            {
                if (string.IsNullOrWhiteSpace(from)
                    || string.IsNullOrWhiteSpace(to)
                    || !DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedFrom)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTo))
                {
                    return Results.BadRequest(new { error = "from and to must be ISO-8601 date-times." });
                }

                return await HandleAsync(new ReconciliationRequest(parsedFrom, parsedTo), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, PaymentService paymentService)
    {
        try
        {
            var report = await paymentService.ReconcileAsync(request.From, request.To);

            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                UnmatchedFinixTransactionIds = report.UnmatchedFinixTransactionIds.ToList(),
                UnmatchedEshopOrderIds = report.UnmatchedEshopOrderIds.ToList()
            };

            response.FinixTransactions.AddRange(report.FinixTransactions.Select(t => new ReconciliationFinixTransactionDto
            {
                Kind = t.Kind,
                Id = t.Id,
                Type = t.Type,
                State = t.State,
                Amount = t.Amount,
                Currency = t.Currency,
                ParentId = t.ParentId,
                CreatedAt = t.CreatedAt,
                MatchedOrderId = t.MatchedOrderId
            }));

            response.EshopOrders.AddRange(report.EshopOrders.Select(o => new ReconciliationOrderDto
            {
                OrderId = o.OrderId,
                OrderDate = o.OrderDate,
                Total = o.Total,
                Currency = o.Currency,
                PaymentStatus = o.PaymentStatus,
                FulfilmentStatus = o.FulfilmentStatus,
                AuthorizationId = o.AuthorizationId,
                CaptureTransferId = o.CaptureTransferId,
                RefundTransferIds = o.RefundTransferIds.ToList()
            }));

            return Results.Ok(response);
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
