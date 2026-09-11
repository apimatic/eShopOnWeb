namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>The payment/fulfilment lifecycle of an order.</summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled; money captured.</summary>
    Captured = 2,

    /// <summary>Captured then partially refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured then fully refunded.</summary>
    Refunded = 4,

    /// <summary>Cancelled before fulfilment; hold released, no money moved.</summary>
    Cancelled = 5,

    /// <summary>A payment operation failed terminally.</summary>
    Failed = 6,
}
