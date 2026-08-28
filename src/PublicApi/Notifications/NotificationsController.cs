using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(
    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController(OrderNotificationApplicationService service) : ControllerBase
{
    [HttpPost("{notificationId:guid}/resend")]
    [ProducesResponseType(typeof(NotificationCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<NotificationCreatedResponse>> Resend(
        Guid notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var createdId = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created($"/api/notifications/{createdId}", new NotificationCreatedResponse(createdId));
    }

    [HttpDelete("{notificationId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeContent(Guid notificationId, CancellationToken cancellationToken)
    {
        await service.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken cancellationToken)
    {
        if (!TryParseOffset(from, out var parsedFrom) || !TryParseOffset(to, out var parsedTo) || parsedFrom >= parsedTo)
            throw new ApiRequestException(StatusCodes.Status400BadRequest, "from and to must be ISO-8601 date-times and from must precede to.");

        return Ok(await service.ReconcileAsync(parsedFrom, parsedTo, cancellationToken));
    }

    private static bool TryParseOffset(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
}
