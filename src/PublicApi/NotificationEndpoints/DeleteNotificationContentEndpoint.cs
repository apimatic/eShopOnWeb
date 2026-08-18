using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: disposes of a message's content. The text is redacted at the
/// provider so it is no longer retrievable there, while the fact the message was sent and what became of it
/// survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _service;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notificationRepository, IOrderNotificationService service)
    {
        _notificationRepository = notificationRepository;
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId));
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request)
    {
        var notification = await _notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        try
        {
            await _service.DisposeContentAsync(notification);
        }
        catch (SmsGatewayException ex)
        {
            // Redaction at the provider failed — the content is NOT gone; tell the operator.
            return ProviderErrorResults.From(ex);
        }

        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = notification.Id
        });
    }
}
