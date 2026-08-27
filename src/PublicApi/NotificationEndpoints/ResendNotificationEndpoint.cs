using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    [FromRoute(Name = "notificationId")]
    public int NotificationId { get; set; }

    [FromBody]
    public ResendNotificationBody Body { get; set; } = new();
}

public class ResendNotificationBody
{
    /// <summary>Caller-supplied key: a repeat under the same key never sends a second message.</summary>
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced (or produced earlier under the same key).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the key was seen before and no new message was sent.</summary>
    public bool Deduplicated { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). Idempotent on
/// the caller-supplied key.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ResendNotificationEndpoint : EndpointBaseAsync
    .WithRequest<ResendNotificationRequest>
    .WithActionResult<ResendNotificationResponse>
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingClient _messagingClient;
    private readonly IAppLogger<ResendNotificationEndpoint> _logger;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notifications, IMessagingClient messagingClient,
        IAppLogger<ResendNotificationEndpoint> logger)
    {
        _notifications = notifications;
        _messagingClient = messagingClient;
        _logger = logger;
    }

    [HttpPost("api/notifications/{notificationId}/resend")]
    [SwaggerOperation(Summary = "Re-sends a failed notification (operator, idempotent)", Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult<ResendNotificationResponse>> HandleAsync(
        ResendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var original = await _notifications.GetByIdAsync(request.NotificationId, cancellationToken);
        if (original is null) return NotFound();

        var prior = await _notifications.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.Body.IdempotencyKey), cancellationToken);
        if (prior is not null)
        {
            return new ResendNotificationResponse
            {
                NotificationId = prior.Id,
                Status = prior.Status,
                Deduplicated = true
            };
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return Conflict(new { error = "The message content has been disposed of and can no longer be sent." });
        }

        OrderNotification resend;
        try
        {
            var message = await _messagingClient.SendMessageAsync(original.DestinationNumber, original.Body, cancellationToken);
            resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationType.Resend,
                original.DestinationNumber, original.Body, message.Sid, message.Status ?? "unknown",
                idempotencyKey: request.Body.IdempotencyKey);
            resend.UpdateProviderState(message.Status ?? "unknown", message.ErrorCode, message.ErrorMessage);
        }
        catch (MessagingProviderException ex)
        {
            _logger.LogError("Resend of notification {NotificationId} failed: HTTP {HttpStatus}, provider error {ProviderErrorCode}",
                original.Id, ex.HttpStatusCode?.ToString() ?? "n/a", ex.ProviderErrorCode?.ToString() ?? "n/a");
            resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationType.Resend,
                original.DestinationNumber, original.Body, providerMessageSid: null, "send-failed",
                idempotencyKey: request.Body.IdempotencyKey);
            resend.UpdateProviderState("send-failed", ex.ProviderErrorCode, ex.GetType().Name);
        }

        await _notifications.AddAsync(resend, cancellationToken);

        return new ResendNotificationResponse
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            Deduplicated = false
        };
    }
}
