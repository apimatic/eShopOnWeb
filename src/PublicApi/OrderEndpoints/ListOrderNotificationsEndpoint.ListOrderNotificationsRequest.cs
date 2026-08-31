using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
