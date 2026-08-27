using System;
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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key (Idempotency-Key header) makes a repeated request return the first
/// attempt's notification instead of sending a second message.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ResendNotificationEndpoint : EndpointBaseAsync
    .WithRequest<ResendNotificationRequest>
    .WithActionResult<ResendNotificationResponse>
{
    private readonly INotificationManagementService _notificationManagementService;

    public ResendNotificationEndpoint(INotificationManagementService notificationManagementService)
    {
        _notificationManagementService = notificationManagementService;
    }

    [HttpPost("api/notifications/{notificationId}/resend")]
    [SwaggerOperation(
        Summary = "Re-sends a notification (operator)",
        Description = "Re-sends a message that did not reach the shopper; safe to retry under the same idempotency key",
        OperationId = "notifications.resend",
        Tags = new[] { "NotificationEndpoints" })
    ]
    public override async Task<ActionResult<ResendNotificationResponse>> HandleAsync(
        ResendNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var resend = await _notificationManagementService.ResendAsync(
            request.NotificationId, request.IdempotencyKey, cancellationToken);

        return new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            MessageSid = resend.MessageSid
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    [FromRoute(Name = "notificationId")]
    public int NotificationId { get; set; }

    [FromHeader(Name = "Idempotency-Key")]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}
