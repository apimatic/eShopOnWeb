using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
}

public class RedactNotificationContentResponse : BaseResponse
{
    public RedactNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public RedactNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
