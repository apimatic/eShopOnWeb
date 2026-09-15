using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — a shopper has asked for the content of a message about
/// them to be disposed of. The text is redacted at the provider (so it is no longer retrievable there, not merely
/// hidden here) and cleared locally, while the fact that the message was sent and what became of it survive.
/// Operator action: administrator role only.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notifier) => await HandleAsync(notificationId, notifier))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notifier)
    {
        try
        {
            // NotificationNotFoundException (-> 404) is mapped by the exception middleware.
            await notifier.DisposeContentAsync(notificationId);
        }
        catch (TwilioApiException ex)
        {
            // The content could not be disposed of at the provider; nothing was changed locally either.
            return Results.Problem(
                title: "The message content could not be disposed of at the provider.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.NoContent();
    }
}
