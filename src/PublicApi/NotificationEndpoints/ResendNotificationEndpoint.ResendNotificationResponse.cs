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
    public int ResendOfId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}
