using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
