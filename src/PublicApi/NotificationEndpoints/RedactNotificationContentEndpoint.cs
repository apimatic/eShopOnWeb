using System.Threading;
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
/// DELETE /api/notifications/{notificationId}/content — operator action on behalf of a shopper
/// who asked for a message's content to be disposed of. The text is redacted at the provider
/// (not merely hidden locally); the fact a message was sent and what became of it survives.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var redacted = await notificationService.RedactContentAsync(notificationId, cancellationToken);
                    return redacted ? Results.NoContent() : Results.NotFound();
                }
                catch (TwilioApiException ex)
                {
                    // The content could not be disposed of at the provider — do not claim success.
                    return Results.Problem(
                        title: "The message content could not be disposed of at the provider.",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }
}
