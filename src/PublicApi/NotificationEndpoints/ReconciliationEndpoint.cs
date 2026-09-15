using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public struct ReconciliationQuery
{
    [FromQuery(Name = "from")] public DateTimeOffset? From { get; set; }
    [FromQuery(Name = "to")] public DateTimeOffset? To { get; set; }
}

/// <summary>
/// Operator action: a report listing the provider's own record of messages for a date range and
/// lining them up against what this app believes it sent — counting only messages sent from this
/// app's configured sending number. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// GET /api/notifications/reconciliation?from={from}&amp;to={to}
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([AsParameters] ReconciliationQuery query, INotificationAdminService service) =>
                await HandleAsync(query, service))
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, INotificationAdminService service)
    {
        if (query.From is null || query.To is null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }
        if (query.From > query.To)
        {
            return Results.BadRequest(new { message = "'from' must not be later than 'to'." });
        }

        var report = await service.ReconcileAsync(query.From.Value, query.To.Value);
        return Results.Ok(report);
    }
}
