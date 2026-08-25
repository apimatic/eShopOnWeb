using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

/// <summary>
/// Operator report: lines up PayPal's own transaction record for a date range against eShop's local
/// orders/payments, so a payment either side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        var report = await reconciliationService.BuildReportAsync(request.From, request.To);

        response.Matched = report.Matched.Select(m => new ReconciliationMatchDto
        {
            OrderId = m.OrderId,
            PayPalOrderId = m.PayPalOrderId,
            AmountMismatch = m.AmountMismatch,
            PayPalTransaction = ToDto(m.PayPalTransaction)
        }).ToList();

        response.PayPalOnly = report.PayPalOnly.Select(ToDto).ToList();
        response.EShopOnly = report.EShopOnly.Select(OrderMapping.ToDto).ToList();

        return Results.Ok(response);
    }

    private static ReconciliationTransactionDto ToDto(ApplicationCore.Interfaces.Payments.GatewayTransaction transaction) => new()
    {
        TransactionId = transaction.TransactionId,
        PayPalReferenceId = transaction.PayPalReferenceId,
        PayPalReferenceIdType = transaction.PayPalReferenceIdType,
        Status = transaction.Status,
        EventCode = transaction.EventCode,
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        UpdatedAt = transaction.UpdatedAt
    };
}
