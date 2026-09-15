using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByBuyerSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}
