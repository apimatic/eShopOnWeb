using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDetailDto : NotificationDto
{
    public string? Body { get; set; }
    public string? ProviderMessageSid { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDetailDto> Notifications { get; set; } = new();
}
