using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(
    Roles = Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created($"/api/orders/{result.OrderId}/notifications", new
        {
            notificationId = result.Id,
            deliveryStatus = result.ProviderStatus,
            providerMessageSid = result.ProviderMessageSid,
            resendOfNotificationId = result.ResendOfNotificationId
        });
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var found = await _service.RedactContentAsync(notificationId, cancellationToken);
        return found ? NoContent() : NotFound();
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var items = await _service.ReconcileAsync(from, to, cancellationToken);
        return Ok(new
        {
            from,
            to,
            count = items.Count,
            items = items.Select(x => new
            {
                notificationId = x.NotificationId,
                providerMessageSid = x.ProviderMessageSid,
                match = x.Match,
                applicationStatus = x.ApplicationStatus,
                providerStatus = x.ProviderStatus,
                providerErrorCode = x.ProviderErrorCode,
                providerCreatedAt = x.ProviderCreatedAt,
                providerSentAt = x.ProviderSentAt
            })
        });
    }
}

public sealed class ResendNotificationRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string IdempotencyKey { get; init; } = string.Empty;
}
