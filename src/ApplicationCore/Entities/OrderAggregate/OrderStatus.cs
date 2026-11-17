namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an order, including the post-checkout payment and fulfilment states.
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Canceled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}
