using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — line up PayPal's own transaction record
/// against eShop's captured orders for a date range. Administrators only. An empty result
/// for a very recent range is expected (PayPal's reporting lags live activity).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService service) =>
                await HandleAsync(new ReconciliationRequest(from, to), service))
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }

        var report = await service.ReconcileAsync(request.From.Value, request.To.Value);
        return Results.Ok(report);
    }
}

public record ReconciliationRequest(DateTimeOffset? From, DateTimeOffset? To);
