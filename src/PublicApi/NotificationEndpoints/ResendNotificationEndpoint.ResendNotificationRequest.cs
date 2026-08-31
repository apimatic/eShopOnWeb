using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key returns the
    /// original attempt instead of sending a second message.
    /// </summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int ResendOfNotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the key had already been used and no new message was sent.</summary>
    public bool IdempotentReplay { get; set; }
}
