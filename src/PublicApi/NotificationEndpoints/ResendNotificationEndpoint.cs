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
/// Re-sends a message that did not reach the shopper (operator action). The caller-supplied
/// idempotency key ensures repeating the request under the same key does not send twice.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, int>
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
                return await HandleAsync(request, notificationId);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, int notificationId)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new ResendNotificationResponse(request.CorrelationId())
            {
                Error = "An idempotency key is required."
            });
        }

        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        switch (result.Status)
        {
            case ResendNotificationStatus.NotFound:
                return Results.NotFound();
            case ResendNotificationStatus.NotResendable:
                return Results.Conflict(new ResendNotificationResponse(request.CorrelationId())
                {
                    Error = "Only messages that failed or were undelivered can be re-sent."
                });
            case ResendNotificationStatus.ContentRedacted:
                return Results.Conflict(new ResendNotificationResponse(request.CorrelationId())
                {
                    Error = "The content of this message has been disposed of and can no longer be sent."
                });
            case ResendNotificationStatus.AlreadyProcessed:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status,
                    AlreadyProcessed = true
                });
            default:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status
                });
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(System.Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public bool AlreadyProcessed { get; set; }
    public string? Error { get; set; }
}
