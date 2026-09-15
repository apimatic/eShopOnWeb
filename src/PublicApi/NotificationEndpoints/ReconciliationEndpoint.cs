using System;
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
/// Operator report: the provider's own record of messages for a date range, lined up against what eShop
/// believes it sent, so a message one side knows about and the other does not is visible. Counts only
/// messages sent from this application's configured sending number.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset?, DateTimeOffset?, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service)
    {
        if (from is null || to is null)
            return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
        if (from > to)
            return Results.BadRequest(new { error = "'from' must not be after 'to'." });

        var report = await service.ReconcileAsync(from.Value, to.Value);
        return Results.Ok(report);
    }
}
