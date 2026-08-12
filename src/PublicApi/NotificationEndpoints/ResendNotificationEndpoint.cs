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
/// Operator re-sends a message that did not reach the shopper. Idempotent under the caller-supplied
/// key: a repeat under the same key does not send again. Administrator-only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        var result = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);
        if (!result.Found)
            return Results.NotFound();
        if (result.Error is not null)
            return Results.BadRequest(new { error = result.Error });

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.NotificationId,
            Sent = result.Sent
        });
    }
}
