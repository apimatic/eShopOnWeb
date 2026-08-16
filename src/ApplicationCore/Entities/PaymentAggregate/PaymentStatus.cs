namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement for an order, mirroring the PayPal state the app owns a copy of.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no authorization taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization CREATED) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>Captured, then partially returned to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then fully returned to the shopper.</summary>
    Refunded = 4,

    /// <summary>Cancelled before fulfilment; the hold was released (voided) so no money moved.</summary>
    Cancelled = 5,

    /// <summary>The authorization attempt was declined or otherwise failed.</summary>
    Failed = 6
}
