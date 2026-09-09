namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle state of the money movement attached to an <c>Order</c>. This is additive to the
/// existing order model — an order row can exist with no payment (legacy flow) or with one
/// <see cref="OrderPayment"/> tracking the PayPal-owned state.
/// </summary>
public enum PaymentStatus
{
    /// <summary>The order has been placed but no authorization has been taken yet.</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) in full at fulfilment.</summary>
    Captured = 2,

    /// <summary>Captured, then partially refunded — less than the captured amount returned.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then fully refunded.</summary>
    Refunded = 4,

    /// <summary>The hold was released before capture; no money moved.</summary>
    Cancelled = 5,

    /// <summary>The authorization or capture failed at the processor.</summary>
    Failed = 6
}
