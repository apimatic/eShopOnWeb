using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class GetNotificationsReconciliationEndpoint : IEndpoint<IResult, HttpContext, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderLifecycleService service) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromUtc) || !DateTimeOffset.TryParse(to, out var toUtc))
                {
                    throw new BadRequestException("from and to must be ISO-8601 date-times.");
                }

                var report = await service.ReconcileAsync(fromUtc, toUtc);
                return Results.Ok(NotificationDtoFactory.FromReconciliation(report));
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext http, IOrderLifecycleService service)
        => Task.FromResult(Results.Ok());
}
