using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _notificationService;
    private readonly IReadRepository<OrderNotification> _notifications;
    private readonly ITwilioGateway _twilio;

    public NotificationsController(IOrderNotificationService notificationService,
        IReadRepository<OrderNotification> notifications, ITwilioGateway twilio)
    {
        _notificationService = notificationService;
        _notifications = notifications;
        _twilio = twilio;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<ResendNotificationResponse>> ResendAsync(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey,
                cancellationToken);
            return Ok(new ResendNotificationResponse(notification.Id));
        }
        catch (NotificationOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        try
        {
            await _notificationService.DisposeContentAsync(notificationId, cancellationToken);
            return NoContent();
        }
        catch (NotificationOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (TwilioRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider could not confirm content disposal; local content was retained.");
        }
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> ReconcileAsync([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from > to)
        {
            ModelState.AddModelError(nameof(from), "from and to must be ISO-8601 date-times and from must not exceed to.");
            return ValidationProblem(ModelState);
        }

        IReadOnlyList<ProviderMessage> providerMessages;
        try
        {
            providerMessages = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider reconciliation feed is unavailable.");
        }

        var localNotifications = await _notifications.ListAsync(
            new NotificationsCreatedInRangeSpec(from, to), cancellationToken);
        var providerBySid = providerMessages
            .Where(message => !string.IsNullOrEmpty(message.Sid))
            .GroupBy(message => message.Sid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var localBySid = localNotifications
            .Where(notification => !string.IsNullOrEmpty(notification.ProviderMessageSid))
            .GroupBy(notification => notification.ProviderMessageSid!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var provider in providerMessages)
        {
            localBySid.TryGetValue(provider.Sid, out var local);
            entries.Add(new ReconciliationEntry(provider.Sid, local?.Id, "provider",
                provider.Status, local?.ProviderStatus,
                local == null ? "provider_only" : "matched",
                provider.DateCreated, provider.DateSent, local?.CreatedAt));
        }

        foreach (var local in localNotifications.Where(notification =>
                     string.IsNullOrEmpty(notification.ProviderMessageSid) ||
                     !providerBySid.ContainsKey(notification.ProviderMessageSid)))
        {
            entries.Add(new ReconciliationEntry(local.ProviderMessageSid, local.Id, "application",
                null, local.ProviderStatus, "application_only", null, null, local.CreatedAt));
        }

        return Ok(new ReconciliationResponse(from, to, entries.Count,
            entries.OrderBy(entry => entry.ApplicationCreatedAt ?? entry.ProviderCreatedAt).ToArray()));
    }
}

public sealed class ResendNotificationRequest
{
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, int Count,
    ReconciliationEntry[] Entries);
public sealed record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string Source,
    string? ProviderStatus,
    string? ApplicationStatus,
    string Match,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt,
    DateTimeOffset? ApplicationCreatedAt);
