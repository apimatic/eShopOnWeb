using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationOperationsController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly OrderNotificationService _notifications;
    private readonly ITwilioMessagingClient _messaging;

    public NotificationOperationsController(CatalogContext db, OrderNotificationService notifications,
        ITwilioMessagingClient messaging)
    {
        _db = db;
        _notifications = notifications;
        _messaging = messaging;
    }

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification == null) return NotFound();
        try
        {
            var resend = await _notifications.ResendAsync(notification, request.IdempotencyKey.Trim(),
                cancellationToken);
            return Ok(new { notificationId = resend.Id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.FirstOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification == null) return NotFound();
        try
        {
            await _notifications.DisposeContentAsync(notification, cancellationToken);
            return NoContent();
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502, title: "The provider could not dispose of the message content.");
        }
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
            return BadRequest(new { errors = new { range = new[] { "'from' must be earlier than 'to'." } } });

        try
        {
            var provider = (await _messaging.ListAsync(from, to, cancellationToken))
                .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
                .ToList();
            var providerSidList = provider.Select(x => x.Sid).ToList();
            var application = await _db.OrderNotifications
                .Where(x => (x.CreatedAt >= from && x.CreatedAt <= to) ||
                            (x.ProviderMessageSid != null && providerSidList.Contains(x.ProviderMessageSid)))
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            var applicationBySid = application.Where(x => x.ProviderMessageSid != null)
                .ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
            var providerSids = provider.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);
            var rows = provider.Select(message =>
            {
                applicationBySid.TryGetValue(message.Sid, out var local);
                return new
                {
                    match = local == null ? "provider-only" : "matched",
                    providerMessageSid = message.Sid,
                    notificationId = local?.Id,
                    providerStatus = message.Status,
                    applicationStatus = local?.ProviderStatus,
                    providerDate = message.DateSent ?? message.DateCreated,
                    applicationDate = local?.CreatedAt
                };
            }).Cast<object>().Concat(application.Where(x => x.ProviderMessageSid == null ||
                                                             !providerSids.Contains(x.ProviderMessageSid))
                .Select(local => (object)new
                {
                    match = "application-only",
                    providerMessageSid = local.ProviderMessageSid,
                    notificationId = (int?)local.Id,
                    providerStatus = (string?)null,
                    applicationStatus = local.ProviderStatus,
                    providerDate = (DateTimeOffset?)null,
                    applicationDate = (DateTimeOffset?)local.CreatedAt
                })).ToList();

            return Ok(new { from, to, entries = rows });
        }
        catch (Exception ex) when (ex is TwilioProviderException or HttpRequestException or TaskCanceledException)
        {
            return Problem(statusCode: 502, title: "The reconciliation report could not be obtained from the provider.");
        }
    }
}

public sealed class ResendNotificationRequest
{
    [Required, MinLength(1), MaxLength(200)] public string IdempotencyKey { get; set; } = string.Empty;
}
