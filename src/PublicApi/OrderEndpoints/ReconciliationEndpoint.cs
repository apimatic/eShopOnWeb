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
/// Operator report: lines up PayPal's own transaction records for a date range against eShop's
/// orders, so a payment either side knows about and the other doesn't is visible.
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
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        var report = await reconciliationService.GetReportAsync(request.From, request.To, CancellationToken.None);

        response.From = report.From;
        response.To = report.To;
        response.Entries = report.Entries.Select(e => new ReconciliationEntryDto
        {
            MatchStatus = e.MatchStatus.ToString(),
            PayPalTransactionId = e.PayPalTransactionId,
            PayPalOrderId = e.PayPalOrderId,
            OrderId = e.OrderId,
            PayPalAmount = e.PayPalAmount,
            EShopAmount = e.EShopAmount,
            PayPalStatus = e.PayPalStatus,
            EShopStatus = e.EShopStatus
        }).ToList();

        return Results.Ok(response);
    }
}
