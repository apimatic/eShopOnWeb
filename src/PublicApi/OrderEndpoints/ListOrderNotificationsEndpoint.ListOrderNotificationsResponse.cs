using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}
