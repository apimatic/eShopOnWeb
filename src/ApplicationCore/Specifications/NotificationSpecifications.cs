using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}

public class ContactNumberByBuyerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == phoneNumber);
    }
}

public class OrderNotificationsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

public class PendingOrderNotificationsSpecification : Specification<OrderNotification>
{
    public PendingOrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Status == "scheduled");
    }
}

public class PendingNotificationsForNumberSpecification : Specification<OrderNotification>
{
    public PendingNotificationsForNumberSpecification(string phoneNumber)
    {
        Query.Where(n => n.ToNumber == phoneNumber && n.Status == "scheduled");
    }
}

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
