using System;
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
/// Operator action: PayPal's own record of transactions over a date range, lined up
/// against eShop orders. Covers the whole range, not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(from, to, reconciliationService);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        var report = await reconciliationService.GetReportAsync(from, to);
        return Results.Ok(report);
    }
}
