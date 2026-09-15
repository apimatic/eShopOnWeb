using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationCoordinator _coordinator;

    public NotificationsController(OrderNotificationCoordinator coordinator) => _coordinator = coordinator;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IResult> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _coordinator.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.Outcome switch
        {
            ResendOutcome.Success => Results.Created(
                $"/api/notifications/{result.NotificationId}",
                new { notificationId = result.NotificationId }),
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.Invalid => Results.BadRequest(new { error = "An idempotency key of at most 200 characters is required." }),
            ResendOutcome.InProgress => Results.Conflict(new { error = "The original idempotent request has not completed." }),
            ResendOutcome.ContentDisposed => Results.Conflict(new { error = "The message content has been disposed of." }),
            ResendOutcome.ContactRemoved => Results.Conflict(new { error = "The destination contact number has been removed." }),
            _ => Results.Conflict(new { error = "Only a failed or undelivered message can be resent." })
        };
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var outcome = await _coordinator.DisposeContentAsync(notificationId, cancellationToken);
        return outcome switch
        {
            DisposeContentOutcome.Success => Results.NoContent(),
            DisposeContentOutcome.NotFound => Results.NotFound(),
            _ => Results.Problem(
                "The provider did not confirm disposal, so the local content was retained.",
                statusCode: StatusCodes.Status502BadGateway)
        };
    }

    [HttpGet("reconciliation")]
    public async Task<IResult> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) return Results.BadRequest(new { error = "The 'from' value must precede 'to'." });
        try
        {
            var entries = await _coordinator.ReconcileAsync(from, to, cancellationToken);
            return Results.Ok(new
            {
                from,
                to,
                entries = entries.Select(x => new
                {
                    providerMessageId = x.ProviderMessageSid,
                    match = x.Match,
                    provider = x.Provider is null ? null : new
                    {
                        status = x.Provider.Status,
                        sentAt = x.Provider.DateSent,
                        createdAt = x.Provider.DateCreated,
                        errorCode = x.Provider.ErrorCode,
                        errorMessage = x.Provider.ErrorMessage
                    },
                    eshop = x.Eshop is null ? null : new
                    {
                        notificationId = x.Eshop.Id,
                        orderId = x.Eshop.OrderId,
                        status = x.Eshop.ProviderStatus,
                        createdAt = x.Eshop.CreatedAt
                    }
                })
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("The provider reconciliation request failed.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
