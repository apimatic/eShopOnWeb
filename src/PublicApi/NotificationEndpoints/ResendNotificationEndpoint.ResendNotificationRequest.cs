using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequestBody
{
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRequest : BaseRequest
{
    public ResendNotificationRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }

    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string OperatorId { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>
    /// The identifier of the message the resend produced.
    /// </summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}
