using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced (new or previously created under the same key).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Replayed { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsService _smsService;

    public ResendNotificationEndpoint(IRepository<Notification> notificationRepository, ISmsService smsService)
    {
        _notificationRepository = notificationRepository;
        _smsService = smsService;
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
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.IdempotencyKey));
        if (existing is not null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = existing.Id,
                Status = existing.Status,
                Replayed = true
            });
        }

        if (original.BodyRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The message content has been disposed of and can no longer be sent." });
        }

        SmsSendResult result;
        try
        {
            result = await _smsService.SendMessageAsync(original.ToNumber, original.Body);
        }
        catch (SmsProviderException ex)
        {
            return Results.Json(new { message = $"The provider rejected the resend (error code {ex.ProviderErrorCode})." }, statusCode: 502);
        }

        var resend = await _notificationRepository.AddAsync(new Notification(
            original.OrderId, original.BuyerId, original.ToNumber, NotificationType.Resend,
            original.Body, result.MessageSid, result.Status,
            idempotencyKey: request.IdempotencyKey, resendOfNotificationId: original.Id));

        return Results.Created($"api/notifications/{resend.Id}", new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            Replayed = false
        });
    }
}
