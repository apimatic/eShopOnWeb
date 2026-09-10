namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Lifecycle of the money movement that follows an order. This is additive state that the base
/// eShop <see cref="OrderAggregate.Order"/> does not carry.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed; no hold on the shopper's money yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding the funds (authorization created); nothing captured yet.</summary>
    Authorized = 1,

    /// <summary>Funds have been taken at fulfilment (capture completed).</summary>
    Captured = 2,

    /// <summary>Some — but not all — of the captured amount has been returned.</summary>
    PartiallyRefunded = 3,

    /// <summary>The whole captured amount has been returned.</summary>
    Refunded = 4,

    /// <summary>The hold was released before fulfilment; no money ever moved.</summary>
    Cancelled = 5,

    /// <summary>The authorization attempt did not succeed.</summary>
    Failed = 6
}
