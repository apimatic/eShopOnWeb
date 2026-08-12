using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — disposes of a message's text at the provider
/// (redaction) so it is no longer retrievable there, while the fact it was sent and what became of it
/// survives. Operator-only.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderNotificationService _orderNotifications;

    public DisposeNotificationContentEndpoint(IOrderNotificationService orderNotifications)
    {
        _orderNotifications = orderNotifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http) =>
            {
                return await HandleAsync(notificationId, http);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        try
        {
            var found = await _orderNotifications.DisposeContentAsync(notificationId, http.RequestAborted);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (TwilioApiException ex)
        {
            // The text must no longer be retrievable at the provider; if redaction failed we do not
            // claim success. Nothing was marked disposed locally.
            return Results.Problem(
                title: "The provider could not dispose of the message content.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
