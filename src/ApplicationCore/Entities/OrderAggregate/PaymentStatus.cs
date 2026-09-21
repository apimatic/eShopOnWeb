namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment lifecycle of an <see cref="Order"/>. This is additive state layered
/// onto the existing order model — a brand-new order starts <see cref="AwaitingPayment"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled: the authorized funds have been captured (money taken).</summary>
    Paid = 2,

    /// <summary>Cancelled before fulfilment: the authorization was voided, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
