using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public bool AlreadyExisted { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
/// idempotency key makes a repeated request safe: the same key never produces a second send.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, CancellationToken>
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly TwilioMessagingService _messaging;
    private readonly ILogger<ResendNotificationEndpoint> _logger;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notifications,
        IRepository<ContactNumber> contactNumbers,
        TwilioMessagingService messaging,
        ILogger<ResendNotificationEndpoint> logger)
    {
        _notifications = notifications;
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, request, ct);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return Results.NotFound();
        }

        var priorAttempts = await _notifications.ListAsync(
            new NotificationByIdempotencyKeySpecification(request.IdempotencyKey), ct);
        var existing = priorAttempts.FirstOrDefault();
        if (existing is not null)
        {
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = existing.Id,
                Status = existing.Status,
                ProviderMessageSid = existing.ProviderMessageSid,
                AlreadyExisted = true
            });
        }

        if (original.ContentRedacted)
        {
            return Results.Conflict(new { message = "The message content has been disposed of and can no longer be sent." });
        }

        // The destination must still be a registered number of the shopper — a removed number
        // is never sent to again.
        ContactNumber? destination = null;
        if (original.ContactNumberId.HasValue)
        {
            destination = await _contactNumbers.GetByIdAsync(original.ContactNumberId.Value, ct);
        }
        if (destination is null || destination.OwnerId != original.BuyerId)
        {
            return Results.Conflict(new { message = "The destination number is no longer on file for this shopper." });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, destination.Id,
            destination.PhoneNumber, NotificationKind.Resend, original.Body,
            idempotencyKey: request.IdempotencyKey);
        await _notifications.AddAsync(resend, ct);

        try
        {
            var sent = await _messaging.SendMessageAsync(destination.PhoneNumber, original.Body, ct);
            resend.MarkAccepted(sent.Sid, sent.Status);
        }
        catch (MessagingException ex)
        {
            _logger.LogWarning(ex, "Resend notification {NotificationId} could not be sent (provider status {ProviderStatus}).",
                resend.Id, (int?)ex.ProviderStatusCode);
            resend.MarkSendFailed(ex.Message);
        }
        await _notifications.UpdateAsync(resend, ct);

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            ProviderMessageSid = resend.ProviderMessageSid,
            AlreadyExisted = false
        });
    }
}
