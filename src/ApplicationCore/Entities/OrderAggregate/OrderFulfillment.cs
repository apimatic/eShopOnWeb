using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderFulfillment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderFulfillment() { }
#pragma warning restore CS8618

    public OrderFulfillment(int orderId)
    {
        ForOrderId = orderId;
        Status = OrderStatus.Placed;
    }

    public int ForOrderId { get; private set; }
    public OrderStatus Status { get; private set; }

    public void MarkDispatched()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new OrderStateException("A cancelled order cannot be dispatched.");
        }

        Status = OrderStatus.Dispatched;
    }

    public void MarkCancelled()
    {
        Status = OrderStatus.Cancelled;
    }
}
