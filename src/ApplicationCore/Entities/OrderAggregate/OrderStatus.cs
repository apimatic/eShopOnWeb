namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment lifecycle of an <see cref="Order"/>.
/// This is additive state layered on top of the original catalog/basket/order flow:
/// an order is created <see cref="AwaitingPayment"/>, moves to <see cref="PaymentAuthorized"/>
/// once funds are held at PayPal, and to <see cref="Fulfilled"/> once they are captured.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 1,

    /// <summary>The order total is held (authorized) at PayPal, but not yet taken.</summary>
    PaymentAuthorized = 2,

    /// <summary>The order has been fulfilled and the held funds have been captured.</summary>
    Fulfilled = 3,

    /// <summary>A fulfilled order that has had part of its captured amount refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>A fulfilled order whose entire captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 6
}
