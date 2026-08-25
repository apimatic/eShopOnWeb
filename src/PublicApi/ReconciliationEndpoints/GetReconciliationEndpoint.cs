using System;
using System.Linq;
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
/// Operator action: lines up PayPal's own transaction records for a date range against this app's
/// orders, so a payment either side knows about and the other doesn't is visible.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), reconciliationService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var response = new GetReconciliationResponse(request.CorrelationId());

        var entries = await reconciliationService.GetReconciliationReportAsync(request.From, request.To);
        response.Entries = entries.Select(e => new ReconciliationEntryDto
        {
            PayPalTransactionId = e.PayPalTransactionId,
            OrderId = e.OrderId,
            PayPalAmount = e.PayPalAmount,
            EShopAmount = e.EShopAmount,
            PayPalStatus = e.PayPalStatus,
            MatchStatus = e.MatchStatus
        }).ToList();
        return Results.Ok(response);
    }
}
