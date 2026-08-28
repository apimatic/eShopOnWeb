using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<ActionResult<ResendNotificationResponse>> Resend(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken ct)
    {
        try
        {
            var resendId = await _service.ResendAsync(notificationId, request.IdempotencyKey, ct);
            return resendId is null ? NotFound() : Ok(new ResendNotificationResponse(resendId.Value));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (NotificationConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken ct)
    {
        try
        {
            return await _service.DisposeContentAsync(notificationId, ct) ? NoContent() : NotFound();
        }
        catch (TwilioProviderException ex)
        {
            return ProviderProblem(ex);
        }
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _service.ReconcileAsync(from, to, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
        catch (TwilioProviderException ex)
        {
            return ProviderProblem(ex);
        }
    }

    private ObjectResult ProviderProblem(TwilioProviderException ex)
    {
        var status = ex.StatusCode is null || (int)ex.StatusCode >= 500
            ? StatusCodes.Status502BadGateway
            : (int)ex.StatusCode;
        if (status is 401 or 403) status = StatusCodes.Status502BadGateway;
        if (status == 429) status = StatusCodes.Status503ServiceUnavailable;
        return StatusCode(status, new ProblemDetails { Title = ex.Message, Status = status });
    }
}
