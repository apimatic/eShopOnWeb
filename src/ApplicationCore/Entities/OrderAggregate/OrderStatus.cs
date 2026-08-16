namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment. This is additive state that the
/// original eShopOnWeb order did not carry; the catalog/basket/checkout flow is unaffected.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total has been authorized (held) on the shopper's card, not yet captured.</summary>
    Authorized = 1,

    /// <summary>An operator fulfilled the order; the held funds have been captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released, so no money moved.</summary>
    Cancelled = 3,

    /// <summary>Part of the captured amount has been refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>The full captured amount has been refunded.</summary>
    Refunded = 5
}
