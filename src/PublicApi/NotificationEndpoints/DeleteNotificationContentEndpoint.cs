using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's text after a shopper's request. The body is
/// redacted at the provider (not merely hidden here) and cleared locally, while the fact
/// that a message was sent and its delivery outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(notificationId);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Results.Ok(new { notificationId = notification.Id, contentRedacted = true });
        }

        try
        {
            await _notificationService.RedactContentAsync(notification);
        }
        catch (MessageProviderException)
        {
            // The provider could not redact the body right now; nothing is hidden
            // locally either, so the operator can safely retry.
            return Results.Problem("The provider could not dispose of the message content; please retry.", statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new { notificationId = notification.Id, contentRedacted = true });
    }
}
