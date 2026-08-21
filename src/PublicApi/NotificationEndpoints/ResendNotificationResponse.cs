using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
}
