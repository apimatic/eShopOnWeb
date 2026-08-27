using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, INotificationOperationsService operationsService) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey), operationsService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperationsService operationsService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        var result = await operationsService.ResendAsync(request.NotificationId, request.IdempotencyKey);

        return result.Outcome switch
        {
            ResendOutcome.Sent => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                IdempotentReplay = result.IsIdempotentReplay
            }),
            ResendOutcome.NotificationNotFound => Results.NotFound(new { error = result.Error }),
            ResendOutcome.DestinationNoLongerRegistered => Results.Conflict(new { error = result.Error }),
            _ => Results.Conflict(new { error = result.Error })
        };
    }
}

public class ResendNotificationRequestBody
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; }
    public string? IdempotencyKey { get; }

    public ResendNotificationRequest(int notificationId, string? idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}
