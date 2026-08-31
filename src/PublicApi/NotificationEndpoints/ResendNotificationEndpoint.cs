using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey));
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required." });
        }

        try
        {
            var result = await _orderNotificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
            var response = new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification.Id,
                OrderId = result.Notification.OrderId,
                Status = result.Notification.Status,
                MessageSid = result.Notification.MessageSid,
                IdempotentReplay = result.IdempotentReplay
            };
            return result.IdempotentReplay
                ? Results.Ok(response)
                : Results.Created($"api/notifications/{result.Notification.Id}", response);
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class ResendNotificationRequestBody
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public ResendNotificationRequest(int notificationId, string? idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int NotificationId { get; }
    public string? IdempotencyKey { get; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool IdempotentReplay { get; set; }
}
