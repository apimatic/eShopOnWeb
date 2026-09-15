using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationCoordinator _notifications;

    public NotificationsController(OrderNotificationCoordinator notifications) => _notifications = notifications;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, [FromBody] ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notifications.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        if (result.IsMissing) return NotFound();
        if (!result.Succeeded) return Conflict(new { error = result.Error });
        var body = new { notificationId = result.NotificationId };
        return result.WasIdempotentReplay ? Ok(body) : Created($"/api/notifications/{result.NotificationId}", body);
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        var result = await _notifications.DisposeContentAsync(notificationId, cancellationToken);
        if (result.IsMissing) return NotFound();
        if (!result.Succeeded) return Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery(Name = "from")] DateTimeOffset from,
        [FromQuery(Name = "to")] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || from >= to)
            return BadRequest(new { error = "from and to must be valid ISO-8601 date-times and from must precede to." });
        try
        {
            var items = await _notifications.ReconcileAsync(from, to, cancellationToken);
            return Ok(new { from, to, items });
        }
        catch (SmsProviderException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class ResendNotificationRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
