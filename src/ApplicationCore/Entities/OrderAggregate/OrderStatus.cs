namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// Existing eShopOnWeb orders had no such state; this is added additively so the
/// one-time-commerce flow can carry a real payment through to capture or return.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not yet taken.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and the held funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization hold was released.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully refunded after fulfilment.</summary>
    Refunded = 4,

    /// <summary>Captured payment partially refunded after fulfilment.</summary>
    PartiallyRefunded = 5
}
