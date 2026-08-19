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

/// <summary>
/// Operator action: a report over a date range that lists the provider's own record of messages
/// sent from this application's configured number and lines them up against what eShop believes it
/// sent, so a message one side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([AsParameters] ReconciliationRequest request, IOrderNotificationService service) =>
                await HandleAsync(request, service))
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.From == default || request.To == default)
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }
        if (request.To < request.From)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}

public class ReconciliationRequest
{
    /// <summary>Start of the range (ISO-8601 date-time).</summary>
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    /// <summary>End of the range (ISO-8601 date-time).</summary>
    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}
