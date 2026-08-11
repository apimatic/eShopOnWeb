namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle of the money movement attached to an <see cref="Order"/>.
/// eShopOnWeb historically ended checkout by writing an order row and never took payment;
/// this state machine adds the hold-at-checkout / take-at-fulfilment / give-back-on-return flow.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>The authorization was captured at fulfilment; money has moved to the merchant.</summary>
    Paid = 2,

    /// <summary>The authorization was voided before fulfilment; the hold was released and no money moved.</summary>
    Cancelled = 3,

    /// <summary>The captured payment was refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>The captured payment was refunded in full.</summary>
    Refunded = 5
}
