namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment = 0,
    AwaitingFulfilment = 1,
    Fulfilled = 2,
    Cancelled = 3
}
