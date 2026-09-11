using System;
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
/// Lists PayPal's own record of transactions for a date range and lines them up against eShop
/// orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible. Covers
/// the whole range (all pages). Operator action, restricted to administrators. <c>from</c> and
/// <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
                await HandleAsync(from, to, service))
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IReconciliationService service)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.");
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }
}
