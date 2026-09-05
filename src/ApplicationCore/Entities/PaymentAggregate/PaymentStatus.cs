namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Local lifecycle state of the payment attached to an order.
/// </summary>
public enum PaymentStatus
{
    /// <summary>An authorization request is in flight; its outcome is not yet known.</summary>
    Authorizing = 1,

    /// <summary>Funds are held (authorized) but not taken.</summary>
    Authorized = 2,

    /// <summary>The authorization was voided before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>The order was cancelled before any payment existed.</summary>
    Cancelled = 4,

    /// <summary>The authorization failed (e.g. card declined); funds were never held.</summary>
    Failed = 5,

    /// <summary>The payment was captured at fulfilment; money was taken.</summary>
    Captured = 6,

    /// <summary>The captured payment was fully refunded.</summary>
    Refunded = 7,

    /// <summary>The captured payment was partly refunded; further refunds may still fit under the capture.</summary>
    PartiallyRefunded = 8
}
