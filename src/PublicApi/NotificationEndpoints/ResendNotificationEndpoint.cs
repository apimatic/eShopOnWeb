using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The
/// caller-supplied idempotency key (Idempotency-Key header) guarantees a
/// repeated request under the same key does not send a second message.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ResendNotificationEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<ResendNotificationResponse>
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpPost("api/notifications/{notificationId}/resend")]
    [SwaggerOperation(
        Summary = "Re-sends a notification",
        Description = "Re-sends a message that did not reach the shopper. Requires an Idempotency-Key header; repeating the request under the same key does not send a second message.",
        OperationId = "notifications.resend",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult<ResendNotificationResponse>> HandleAsync(
        [FromRoute(Name = "notificationId")] int request, CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest($"The {IdempotencyKeyHeader} header is required.");
        }

        try
        {
            var notification = await _notificationService.ResendAsync(request, idempotencyKey, cancellationToken);
            return new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                Status = notification.Status,
                MessageSid = notification.MessageSid
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (SmsProviderException)
        {
            return StatusCode(502, "The messaging provider could not send the message.");
        }
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}
