namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment / fulfilment lifecycle of an <see cref="OrderAggregate.Order"/>.
/// Additive to the existing order flow: an order is placed <see cref="AwaitingPayment"/>,
/// the money is held on <see cref="Authorized"/>, taken on <see cref="Fulfilled"/>,
/// released on <see cref="Cancelled"/>, and returned on <see cref="PartiallyRefunded"/> / <see cref="Refunded"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created); nothing captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; funds captured from the shopper.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the hold was voided and no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured, then part of the captured amount was refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured, then the full captured amount was refunded.</summary>
    Refunded = 5,

    /// <summary>The authorization attempt failed and no hold exists.</summary>
    Failed = 6
}
