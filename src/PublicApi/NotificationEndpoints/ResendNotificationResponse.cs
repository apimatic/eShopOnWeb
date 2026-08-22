using System;
using Microsoft.eShopWeb.PublicApi;

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
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? SendFailure { get; set; }
}
