using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }

    /// <summary>True when this key was already used and the previously produced message is returned instead of sending again.</summary>
    public bool Replayed { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;
    private readonly ILogger<ResendNotificationEndpoint> _logger;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository, ISmsProvider smsProvider,
        ILogger<ResendNotificationEndpoint> logger)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
        _logger = logger;
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
            return Results.BadRequest(new { error = "idempotencyKey is required." });
        }

        // Repeating a request under the same key must not send a second message.
        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByResendKeySpecification(request.IdempotencyKey));
        if (existing is not null)
        {
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = existing.Id,
                Status = existing.Status,
                ProviderMessageSid = existing.ProviderMessageSid,
                Replayed = true
            });
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId);
        if (original is null)
        {
            return Results.NotFound();
        }
        if (original.ContentRedacted || original.MessageBody is null)
        {
            return Results.Conflict(new { error = "The content of this message has been disposed of and can no longer be sent." });
        }

        // A deleted number must never be sent to again.
        var contactNumber = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdSpecification(original.ContactNumberId));
        if (contactNumber is null)
        {
            return Results.Conflict(new { error = "The contact number this message was addressed to is no longer registered." });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.Type, original.MessageBody, resendIdempotencyKey: request.IdempotencyKey);
        try
        {
            var result = await _smsProvider.SendMessageAsync(contactNumber.PhoneNumber, original.MessageBody);
            if (result.Success && result.MessageSid is not null)
            {
                resend.MarkProviderAccepted(result.MessageSid, result.Status ?? "accepted");
            }
            else
            {
                resend.MarkSendFailed(result.Status ?? "failed", result.ErrorCode, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resend of notification {NotificationId} failed at the provider", notificationId);
            resend.MarkSendFailed("error", null, ex.Message);
        }
        await _notificationRepository.AddAsync(resend);

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            ProviderMessageSid = resend.ProviderMessageSid,
            Replayed = false
        });
    }
}
