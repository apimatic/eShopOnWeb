namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum ShopOrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
