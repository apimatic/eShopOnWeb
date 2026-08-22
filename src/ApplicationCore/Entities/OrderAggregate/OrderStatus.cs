namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    /// <summary>
    /// Legacy storefront checkout with no payment captured through this integration.
    /// </summary>
    Placed = 0,
    AwaitingPayment = 1,
    Authorized = 2,
    Fulfilled = 3,
    Cancelled = 4,
    PartiallyRefunded = 5,
    Refunded = 6
}
