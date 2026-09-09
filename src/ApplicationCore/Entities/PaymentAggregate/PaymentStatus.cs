namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement behind an <see cref="OrderAggregate.Order"/>.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    PendingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Authorization released before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded for less than the captured amount.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment fully refunded.</summary>
    Refunded = 5,

    /// <summary>Authorization or capture failed and cannot proceed.</summary>
    Failed = 6
}
