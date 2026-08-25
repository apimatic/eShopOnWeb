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

/// <summary>Operator report lining PayPal's own transaction record for a date range up against
/// eShop's orders, so discrepancies in either direction are visible.</summary>
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
        var response = new ReconciliationResponse(request.CorrelationId());

        var report = await reconciliationService.BuildReportAsync(request.From, request.To);

        response.From = report.From;
        response.To = report.To;
        response.Entries = report.Entries.Select(e => new ReconciliationEntryDto
        {
            Status = e.Status.ToString(),
            PayPalTransactionId = e.PayPalTransactionId,
            PayPalEventCode = e.PayPalEventCode,
            PayPalStatus = e.PayPalStatus,
            PayPalAmount = e.PayPalAmount,
            OrderId = e.OrderId,
            EShopReference = e.EShopReference,
            EShopAmount = e.EShopAmount,
            CurrencyCode = e.CurrencyCode
        }).ToList();

        return Results.Ok(response);
    }
}
