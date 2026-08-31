using System;
using System.ComponentModel.DataAnnotations;
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
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
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
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        return result.Failure switch
        {
            ResendFailure.NotFound => Results.NotFound(),
            ResendFailure.ContentDisposed => Results.Conflict("The message content has been disposed of and can no longer be sent."),
            ResendFailure.NothingToResend => Results.Conflict("The message cannot be re-sent."),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Notification = NotificationMapping.ToDto(result.Notification),
                AlreadyExisted = result.AlreadyExisted
            })
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public OrderNotificationDto? Notification { get; set; }

    /// <summary>True when this idempotency key already produced a resend; nothing new was sent.</summary>
    public bool AlreadyExisted { get; set; }
}
