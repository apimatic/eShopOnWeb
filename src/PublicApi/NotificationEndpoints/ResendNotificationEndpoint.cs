using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The
/// caller-supplied idempotency key makes repeats safe: the same key never sends
/// a second message; a fresh key is a genuine new attempt.
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
            (int notificationId, ResendNotificationRequest request, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request.WithNotificationId(notificationId), cancellationToken);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request)
        => HandleAsync(request, default);

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        var result = await _notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey, cancellationToken);

        return result.Outcome switch
        {
            ResendNotificationOutcome.Sent => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                Duplicate = false
            }),
            ResendNotificationOutcome.Duplicate => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                Duplicate = true
            }),
            ResendNotificationOutcome.NotificationNotFound => Results.NotFound(),
            ResendNotificationOutcome.ContentDisposed => Results.Conflict(new
            {
                error = "The content of this message has been disposed of; it can no longer be re-sent."
            }),
            ResendNotificationOutcome.DestinationNoLongerRegistered => Results.Conflict(new
            {
                error = "The destination number is no longer registered to the shopper; nothing may be sent to it."
            }),
            _ => Results.StatusCode(500)
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key; repeating the request under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public ResendNotificationRequest WithNotificationId(int notificationId)
    {
        NotificationId = notificationId;
        return this;
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the idempotency key was already used and this is the earlier resend.</summary>
    public bool Duplicate { get; set; }
}
