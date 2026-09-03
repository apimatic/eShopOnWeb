namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. This is the payment/fulfilment state that the
/// existing <see cref="OrderAggregate.Order"/> aggregate does not carry on its own.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled: the authorization was captured and the money taken.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment: the hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 5
}
