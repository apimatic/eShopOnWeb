using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationOrchestrator>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationOrchestrator orchestrator) =>
            {
                return await HandleAsync(notificationId, request, orchestrator);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationOrchestrator orchestrator)
    {
        return HandleAsync(0, request, orchestrator);
    }

    private async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest request,
        IOrderNotificationOrchestrator orchestrator)
    {
        var result = await orchestrator.ResendAsync(notificationId, request.IdempotencyKey);
        return result.ToHttpResult(notification =>
        {
            var response = new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                Notification = notification.ToDto()
            };
            return Results.Ok(response);
        });
    }
}
