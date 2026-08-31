using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }

    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public bool IdempotentReplay { get; set; }
}
