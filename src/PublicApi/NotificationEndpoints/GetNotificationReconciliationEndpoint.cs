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

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notifications);
            })
            .Produces<NotificationReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notifications)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await notifications.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);
