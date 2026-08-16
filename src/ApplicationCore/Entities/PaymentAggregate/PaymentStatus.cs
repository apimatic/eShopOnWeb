namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment lifecycle state of an order's payment.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 3,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 4,

    /// <summary>The authorization was voided before fulfilment; no money moved.</summary>
    Cancelled = 5
}
