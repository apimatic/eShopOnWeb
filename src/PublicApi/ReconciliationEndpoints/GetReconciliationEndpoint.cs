using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, surfacing entries known only to one side.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, HttpContext>
{
    private readonly IReconciliationService _reconciliationService;

    public GetReconciliationEndpoint(IReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext httpContext) =>
            {
                return await HandleAsync(from, to, httpContext);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, HttpContext httpContext)
    {
        if (from == default || to == default)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' (ISO-8601 date-times) are required." });
        }

        try
        {
            var report = await _reconciliationService.GetReportAsync(from, to);
            return Results.Ok(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}
