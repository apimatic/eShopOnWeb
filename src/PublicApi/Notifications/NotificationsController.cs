using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        if (!result.Succeeded)
        {
            return ToProblem(result.Error, result.Message!);
        }

        var response = new ResendNotificationResponse(result.Value!.NotificationId);
        return result.Value.IsReplay
            ? Ok(response)
            : Created($"/api/notifications/{response.NotificationId}", response);
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DeleteContent(int notificationId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteContentAsync(notificationId, cancellationToken);
        return result.Succeeded ? NoContent() : ToProblem(result.Error, result.Message!);
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "'from' must not be later than 'to'.");
        }

        try
        {
            return Ok(await _service.ReconcileAsync(from, to, cancellationToken));
        }
        catch (Exception ex) when (ex is TwilioApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "The provider reconciliation request failed.");
        }
    }

    private ObjectResult ToProblem(OperationError error, string message) => Problem(
        statusCode: error switch
        {
            OperationError.Invalid => StatusCodes.Status400BadRequest,
            OperationError.NotFound => StatusCodes.Status404NotFound,
            OperationError.Conflict => StatusCodes.Status409Conflict,
            OperationError.ProviderUnavailable => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        },
        title: message);
}
