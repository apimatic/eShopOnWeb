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
/// Operator report reconciling PayPal's transaction record for a date range against eShop orders.
/// It covers the whole range (all pages of PayPal's report). Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, reconciliationService);
            })
            .Produces<ReconciliationResult>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReconciliationService reconciliationService)
    {
        var result = await reconciliationService.ReconcileAsync(query.From, query.To);
        return Results.Ok(result);
    }
}

public class ReconciliationQuery
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
