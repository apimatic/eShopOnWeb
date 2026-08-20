namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderLifecycleStatus
{
    Placed = 0,
    AwaitingPayment = 1,
    Authorized = 2,
    Fulfilled = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
