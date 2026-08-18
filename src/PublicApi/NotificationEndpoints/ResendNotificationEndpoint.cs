using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries a caller-supplied
/// idempotency key — repeating a request under the same key returns the message the first attempt produced
/// (no second message goes out), while a genuine second attempt under a fresh key sends again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IRepository<Notification> repository, ISmsProvider smsProvider, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, request, repository, smsProvider, ct);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest request,
        IRepository<Notification> repository,
        ISmsProvider smsProvider,
        CancellationToken ct)
    {
        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        // Idempotent replay: a resend already produced under this key returns that same message — nothing new is sent.
        var alreadyDone = await repository.FirstOrDefaultAsync(new NotificationByIdempotencyKeySpecification(idempotencyKey), ct);
        if (alreadyDone is not null)
        {
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = alreadyDone.Id,
                DeliveryStatus = alreadyDone.DeliveryStatus,
                Duplicate = true
            });
        }

        var original = await repository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return Results.NotFound();
        }

        if (original.ContentDisposed || string.IsNullOrEmpty(original.Body))
        {
            return Results.Conflict(new { message = "The message content is unavailable (disposed) and cannot be resent." });
        }

        // A fresh notification records the resent message with its own SID and status.
        var resend = Notification.CreateImmediate(original.OrderId, original.BuyerId, original.Recipient, original.Type, original.Body);
        resend.MarkResend(idempotencyKey);

        try
        {
            var result = await smsProvider.SendAsync(original.Recipient, original.Body, ct);
            resend.MarkAccepted(result.Sid, result.Status);
        }
        catch (SmsProviderException ex)
        {
            // Nothing was produced — do not record the key, so a genuine retry under the same key may try again.
            return ProviderErrorResults.From(ex);
        }

        await repository.AddAsync(resend, ct);

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resend.Id,
            DeliveryStatus = resend.DeliveryStatus,
            Duplicate = false
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. The same key never sends a second message; a fresh key does.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>The identifier of the message the resend produced (top-level). On a replay, the original produced message.</summary>
    public int NotificationId { get; set; }

    public string? DeliveryStatus { get; set; }

    /// <summary>True when this request matched a prior resend under the same key and sent nothing new.</summary>
    public bool Duplicate { get; set; }
}
