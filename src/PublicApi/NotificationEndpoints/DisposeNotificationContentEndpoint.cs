using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

/// <summary>
/// Operator action on a shopper's behalf: disposes of the content of a message about them, so its
/// text is no longer retrievable from the provider either — not merely hidden here — while the fact
/// that a message was sent, and what became of it, survives. Restricted to administrators.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<Notification> notificationRepository,
                INotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, notificationRepository, notificationService, cancellationToken);
            })
            .Produces<DisposeContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IRepository<Notification> notificationRepository,
        INotificationService notificationService, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        try
        {
            await notificationService.DisposeContentAsync(notification, cancellationToken);
        }
        catch (TwilioApiException ex)
        {
            // The provider could not dispose of the content: do not claim success.
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider could not dispose of the message content.", detail: ex.SafeSummary);
        }

        return Results.Ok(new DisposeContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted
        });
    }
}
