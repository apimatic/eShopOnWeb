using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [FromRoute(Name = "notificationId")]
    public int NotificationId { get; set; }

    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRequestBody
{
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
}
