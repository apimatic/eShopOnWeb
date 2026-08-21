using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderWorkflowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderWorkflowService orders) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest(notificationId, request), orders);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderWorkflowService orders)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var notification = await orders.ResendAsync(request.NotificationId, request.Body.IdempotencyKey);
        var response = new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            Notification = NotificationDto.From(notification)
        };
        return Results.Ok(response);
    }
}

public class ResendNotificationRouteRequest : BaseRequest
{
    public ResendNotificationRouteRequest(int notificationId, ResendNotificationRequest body)
    {
        NotificationId = notificationId;
        Body = body;
    }

    public int NotificationId { get; }
    public ResendNotificationRequest Body { get; }
}
