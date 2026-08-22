using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderWithPaymentByIdSpec : OrderWithItemsByIdSpec
{
    public OrderWithPaymentByIdSpec(int orderId) : base(orderId)
    {
    }
}
