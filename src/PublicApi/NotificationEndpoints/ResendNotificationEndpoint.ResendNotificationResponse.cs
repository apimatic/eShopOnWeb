using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderSid { get; set; }
}
