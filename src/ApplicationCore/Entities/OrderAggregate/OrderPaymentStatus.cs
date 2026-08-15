namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an <see cref="Order"/>. This is additive state that the
/// original eShopOnWeb order model did not carry: an order used to end its life the moment it was
/// written. The money movement (authorize -> capture -> refund) and the operator flows
/// (fulfil / cancel) drive these transitions.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed, no hold on the shopper's money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding the order total (authorized) but the money has not been taken.</summary>
    Authorized = 1,

    /// <summary>The operator fulfilled the order; the held funds were captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was released, so no money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled, then part of the captured amount was refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled, then the full captured amount was refunded.</summary>
    Refunded = 5
}
