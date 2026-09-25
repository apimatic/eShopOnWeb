namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of a payment for an order. This is the payment/fulfilment state the
/// existing <see cref="Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order"/>
/// aggregate deliberately does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal; not yet captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled by an operator; the authorized funds were captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released. No money moved.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount was refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount was refunded.</summary>
    Refunded = 5,

    /// <summary>A payment operation failed terminally (e.g. the card was declined).</summary>
    Failed = 6
}
