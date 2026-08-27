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
/// Operator action: re-sends a message that did not reach the shopper. The
/// caller-supplied idempotency key guarantees that repeating the request under
/// the same key does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
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
            (int notificationId, ResendNotificationRequestBody body) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey));
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        var result = await _notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);

        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.DestinationNoLongerRegistered => Results.Conflict(new { error = "The destination number is no longer registered; nothing may be sent to it." }),
            ResendOutcome.AlreadyProcessed => Results.Ok(new ResendNotificationResponse
            {
                NotificationId = result.Notification!.Id,
                IdempotentReplay = true,
                Notification = OrderNotificationDto.FromEntity(result.Notification)
            }),
            _ => Results.Created($"api/notifications/{result.Notification!.Id}", new ResendNotificationResponse
            {
                NotificationId = result.Notification.Id,
                IdempotentReplay = false,
                Notification = OrderNotificationDto.FromEntity(result.Notification)
            })
        };
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
    public int NotificationId { get; set; }
    public bool IdempotentReplay { get; set; }
    public OrderNotificationDto? Notification { get; set; }
}
