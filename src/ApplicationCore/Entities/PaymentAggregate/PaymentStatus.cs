namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of a payment for an order. This is the payment/fulfilment state the base eShop
/// <c>Order</c> never carried.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled: funds captured (taken).</summary>
    Captured = 2,

    /// <summary>Cancelled before fulfilment: the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Some — but not all — of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>A payment attempt failed at the provider.</summary>
    Failed = 6
}
