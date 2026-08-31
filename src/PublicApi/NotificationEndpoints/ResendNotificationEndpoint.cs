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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies
/// an idempotency key; repeating the request under the same key returns the notification
/// the first attempt produced without sending again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository,
                ISmsNotificationClient smsClient, IAppLogger<ResendNotificationEndpoint> logger) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationRepository, smsClient, logger);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationClient smsClient,
        IAppLogger<ResendNotificationEndpoint> logger)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var alreadyProcessed = await notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(request.IdempotencyKey));
        if (alreadyProcessed is not null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = alreadyProcessed.Id,
                Status = alreadyProcessed.Status,
                MessageSid = alreadyProcessed.MessageSid,
                AlreadyProcessed = true
            });
        }

        var original = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        // The local status can lag the provider (there is no callback into this app), so
        // refresh it before deciding whether the message is eligible for resend.
        if (!string.IsNullOrEmpty(original.MessageSid) && !NotificationStatus.IsTerminal(original.Status))
        {
            try
            {
                var details = await smsClient.GetMessageAsync(original.MessageSid);
                original.UpdateStatus(details.Status, details.ErrorCode, details.ErrorMessage);
                await notificationRepository.UpdateAsync(original);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not refresh status for notification {NotificationId} before resend: {ErrorType}", original.Id, ex.GetType().Name);
            }
        }

        if (!NotificationStatus.IsResendable(original.Status))
        {
            return Results.Conflict(new { message = $"Only messages that did not reach the shopper can be resent (current status: {original.Status})." });
        }
        if (original.ContentRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of and can no longer be sent." });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ToNumber, original.Type, original.Body, null, request.IdempotencyKey);
        await notificationRepository.AddAsync(resend);

        try
        {
            var result = await smsClient.SendMessageAsync(resend.ToNumber, resend.Body!);
            resend.MarkSubmitted(result.MessageSid!, result.Status);
        }
        catch (Exception ex)
        {
            resend.MarkFailed(NotificationStatus.Failed, null, ex.GetType().Name);
            logger.LogWarning("Resend of notification {NotificationId} failed for new notification {ResendId}: {ErrorType}", original.Id, resend.Id, ex.GetType().Name);
        }
        await notificationRepository.UpdateAsync(resend);

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            MessageSid = resend.MessageSid,
            AlreadyProcessed = false
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool AlreadyProcessed { get; set; }
}
