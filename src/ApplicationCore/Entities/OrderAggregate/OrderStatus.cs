namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no payment or fulfilment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (held) on the shopper's card, but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the held funds have been captured.</summary>
    Paid = 2,

    /// <summary>The order was cancelled before fulfilment; the authorization was voided and no money moved.</summary>
    Cancelled = 3,
}
