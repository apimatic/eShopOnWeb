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
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the caller-supplied key —
/// repeating under the same key returns the message the first attempt produced without sending again. Returns
/// the identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _service;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notificationRepository, IOrderNotificationService service)
    {
        _notificationRepository = notificationRepository;
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request) =>
            {
                return await HandleAsync(notificationId, request);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        try
        {
            var produced = await _service.ResendAsync(notification, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = produced.Id,
                Status = produced.Status
            });
        }
        catch (OrderStatusException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}
