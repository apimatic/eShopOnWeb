using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(new
                    {
                        from,
                        to,
                        entries = await service.ReconcileAsync(from, to, cancellationToken)
                    });
                }
                catch (OrderRequestValidationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    return Results.Json(new { error = "The provider reconciliation request failed." }, statusCode: 503);
                }
            })
            .WithTags("Notifications");
    }
}
