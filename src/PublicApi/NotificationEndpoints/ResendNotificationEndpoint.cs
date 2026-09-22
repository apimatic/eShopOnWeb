using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. Idempotent on the
/// caller-supplied key — a repeat under the same key sends no second message. Restricted to administrators.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ResendNotificationEndpoint : EndpointBaseAsync
    .WithRequest<ResendNotificationRequest>
    .WithActionResult<ResendNotificationResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/notifications/{notificationId}/resend")]
    [SwaggerOperation(
        Summary = "Re-sends a message that did not reach the shopper (operator)",
        Description = "Re-sends a message; idempotent on the caller-supplied key",
        OperationId = "notifications.resend",
        Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult<ResendNotificationResponse>> HandleAsync(
        ResendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Payload is null || string.IsNullOrWhiteSpace(request.Payload.IdempotencyKey))
        {
            return BadRequest("An idempotency key is required.");
        }

        var producedId = await _orderNotificationService.ResendAsync(request.NotificationId, request.Payload.IdempotencyKey, cancellationToken);
        if (producedId is null)
        {
            return NotFound();
        }

        return Ok(new ResendNotificationResponse { NotificationId = producedId.Value });
    }
}
