namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive to the classic eShop
/// flow: orders used to be created and forgotten. Now an order moves from awaiting payment,
/// through an authorization hold, to either fulfilment (money captured) or cancellation.
/// Refund state after fulfilment is derived from the <see cref="Payment"/>, not tracked here.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Operator fulfilled the order; money captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization hold was released.</summary>
    Cancelled = 3
}
