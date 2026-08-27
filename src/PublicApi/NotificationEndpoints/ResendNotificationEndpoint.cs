using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, int>
{
    private readonly IOrderNotificationService _notifications;

    public ResendNotificationEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, ClaimsPrincipal user) =>
            {
                _ = user;
                request ??= new ResendNotificationRequest();
                return await HandleAsync(request, notificationId);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, int notificationId)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required." });
        }

        var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey);
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        if (result.DestinationNoLongerRegistered)
        {
            return Results.BadRequest(new { message = result.Error });
        }

        if (!result.Success || result.NotificationId == null)
        {
            return Results.Json(new { message = result.Error ?? "The notification could not be resent." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new ResendNotificationResponse { NotificationId = result.NotificationId.Value });
    }
}
