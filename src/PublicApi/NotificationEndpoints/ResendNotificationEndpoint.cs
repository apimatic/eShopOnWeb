using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key: repeating a request under the same key sends no second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int NotificationId { get; private set; }

    public void SetNotificationId(int notificationId) => NotificationId = notificationId;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced (the same one on a repeated key).</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. The idempotency key makes a
/// repeat under the same key a no-op; a fresh key is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                request.SetNotificationId(notificationId);
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotency key is required.");

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.ContentDisposed => Results.Conflict("The message content has been disposed and cannot be resent."),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification!.Status
            })
        };
    }
}
