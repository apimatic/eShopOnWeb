using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from=&amp;to= — operator report lining PayPal's transaction ledger up against
/// eShop orders across the whole range. Restricted to administrators. <c>from</c>/<c>to</c> are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
            return Results.BadRequest(new { message = "'from' must be an ISO-8601 date-time." });
        if (!DateTimeOffset.TryParse(request.To, out var to))
            return Results.BadRequest(new { message = "'to' must be an ISO-8601 date-time." });
        if (to < from)
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }
}

public class ReconciliationQuery
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
