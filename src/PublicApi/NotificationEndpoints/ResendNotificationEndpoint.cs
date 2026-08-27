using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message this resend produced (or replayed).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Replayed { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IRepository<OrderNotification>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository,
                ISmsGateway smsGateway, IAppLogger<ResendNotificationEndpoint> logger, CancellationToken cancellationToken) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationRepository, smsGateway, logger, cancellationToken);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository)
        => throw new NotSupportedException("Use the routed overload.");

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway, IAppLogger<ResendNotificationEndpoint> logger, CancellationToken cancellationToken)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(response);
        }

        var original = await notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(request.NotificationId), cancellationToken);
        if (original is null)
        {
            return Results.NotFound();
        }

        var existing = await notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.IdempotencyKey), cancellationToken);
        if (existing is not null)
        {
            // A repeat under the same key: no second message goes out.
            response.NotificationId = existing.Id;
            response.Status = existing.Status;
            response.Replayed = true;
            return Results.Ok(response);
        }

        if (original.ContentDisposed || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of and can no longer be sent." });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber,
            NotificationKind.Resend, original.Body, null, request.IdempotencyKey);

        try
        {
            var result = await smsGateway.SendMessageAsync(original.ToNumber, original.Body, null, cancellationToken);
            if (result.Accepted && result.MessageSid is not null)
            {
                resend.MarkAccepted(result.MessageSid, result.Status ?? "queued");
            }
            else
            {
                resend.MarkSendFailed(result.ErrorMessage ?? "The provider rejected the message.", result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Resend of notification {NotificationId} failed: {Error}", original.Id, ex.Message);
            resend.MarkSendFailed("The messaging provider could not be reached.");
        }

        resend = await notificationRepository.AddAsync(resend, cancellationToken);

        response.NotificationId = resend.Id;
        response.Status = resend.Status;
        return Results.Ok(response);
    }
}
