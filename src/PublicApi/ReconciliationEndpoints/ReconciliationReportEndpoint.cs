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
/// Operator report: lines PayPal's own transactions for [from, to) up against local orders, so a
/// payment either side knows about and the other doesn't is visible. Walks every result page.
/// </summary>
public class ReconciliationReportEndpoint : IEndpoint<IResult, ReconciliationReportRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationReportRequest(from, to), reconciliationService);
            })
            .Produces<ReconciliationReportResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationReportRequest request, IReconciliationService reconciliationService)
    {
        if (request.To <= request.From)
        {
            return Results.BadRequest("'to' must be after 'from'.");
        }

        var report = await reconciliationService.BuildReportAsync(request.From, request.To, default);

        var response = new ReconciliationReportResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Entries = report.Entries.ToList()
        };
        return Results.Ok(response);
    }
}
