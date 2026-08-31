using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The request
/// carries a caller-supplied idempotency key: repeating under the same key
/// returns the original resend without sending a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { errors = new[] { "An idempotency key is required." } });
        }

        var result = await notificationService.ResendNotificationAsync(request.NotificationId, request.IdempotencyKey);
        if (!result.Success)
        {
            return result.Error == "Notification not found."
                ? Results.NotFound()
                : Results.Conflict(new { errors = new[] { result.Error } });
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification!.Id,
            Status = result.Notification.Status,
            AlreadyExisted = result.AlreadyExisted,
            Notification = OrderNotificationDto.FromEntity(result.Notification)
        };
        return Results.Ok(response);
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key; a repeat under the same key must not send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when this key was seen before; no second message was sent.</summary>
    public bool AlreadyExisted { get; set; }
    public OrderNotificationDto? Notification { get; set; }
}
