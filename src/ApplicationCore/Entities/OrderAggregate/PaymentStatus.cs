namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of the money movement attached to an <see cref="Order"/>.
/// This is additive to the original eShopOnWeb order flow, which had no payment state.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization released before fulfilment; no money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Part of a captured payment has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
