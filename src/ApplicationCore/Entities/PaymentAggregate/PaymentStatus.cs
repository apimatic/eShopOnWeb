namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of an order's payment. This is the "payment and fulfilment state"
/// that the base eShopOnWeb <see cref="OrderAggregate.Order"/> does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (PayPal authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; the held funds have been captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Fulfilled and captured, then partly refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>Fulfilled and captured, then refunded in full.</summary>
    Refunded = 4,

    /// <summary>Cancelled before fulfilment; the authorization was voided, no money moved.</summary>
    Cancelled = 5
}
