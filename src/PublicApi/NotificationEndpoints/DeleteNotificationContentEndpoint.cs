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

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notifications;

    public DeleteNotificationContentEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ClaimsPrincipal user) =>
                await HandleAsync(notificationId, user))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ClaimsPrincipal user)
    {
        _ = user;
        var result = await _notifications.RedactContentAsync(notificationId);
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        if (!result.Success)
        {
            return Results.Json(new { message = result.Error ?? "The provider could not redact the message content." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.NoContent();
    }
}
