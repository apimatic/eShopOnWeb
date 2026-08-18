using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: disposes of the content of a message about them. The
/// text is redacted at the provider so it can no longer be retrieved there, while the fact a
/// message was sent, and what became of it, survives. Restricted to administrators.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
                await HandleAsync(notificationId, notificationService))
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public static async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        try
        {
            var notification = await notificationService.DisposeContentAsync(notificationId);
            if (notification is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new DisposeNotificationContentResponse(notification.Id, notification.ContentDisposed));
        }
        catch (SmsGatewayException)
        {
            // The provider could not confirm the redaction, so we do not report success: the
            // content is left intact and the request can be retried.
            return Results.Problem("The provider could not confirm disposal of the message content. Please retry.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public record DisposeNotificationContentResponse(int NotificationId, bool ContentDisposed);
