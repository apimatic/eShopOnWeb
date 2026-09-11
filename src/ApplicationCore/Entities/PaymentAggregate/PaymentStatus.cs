namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of a payment against an order. Mirrors the money movement:
/// authorize (hold) -> capture (take) -> void (release) / refund (give back).
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold on the money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not taken.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured at fulfilment.</summary>
    Fulfilled = 2,

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5,

    /// <summary>A payment operation failed in a way that needs operator attention.</summary>
    Failed = 6
}
