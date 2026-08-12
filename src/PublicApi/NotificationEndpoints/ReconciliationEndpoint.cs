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

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — the provider's own record of messages
/// from this application's sending number over the range, lined up against what eShop believes it sent.
/// from/to are ISO-8601 date-times. Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationOperationsService service, CancellationToken ct) =>
            {
                if (from > to)
                {
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });
                }

                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    // Not used: the route handler binds the from/to query parameters directly.
    public Task<IResult> HandleAsync(INotificationOperationsService service) =>
        Task.FromResult(Results.BadRequest());
}
