namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money attached to an order. Mirrors the sequence of PayPal
/// operations: an authorization places a hold, a capture takes the money at
/// fulfilment, a void releases an un-captured hold, and refunds return captured funds.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no hold taken yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds held (PayPal authorization created); money not yet taken.</summary>
    Authorized = 1,

    /// <summary>Money taken (PayPal authorization captured) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Authorization released before fulfilment; no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been returned to the shopper.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been returned to the shopper.</summary>
    Refunded = 5
}
