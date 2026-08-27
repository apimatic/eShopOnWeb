using System;
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
/// Operator action: disposes of the content of a message about a shopper. The text is
/// redacted at the provider (not merely hidden here) and removed from eShop's record;
/// the fact that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notificationRepository,
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
                return await HandleAsync(new DeleteNotificationContentRequest { NotificationId = notificationId });
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

        if (!notification.ContentRedacted)
        {
            try
            {
                await _notificationService.RedactContentAsync(notification);
            }
            catch (TwilioApiException ex)
            {
                return Results.Problem($"The provider could not redact the message content (error {ex.ErrorCode}).",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted
        });
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
