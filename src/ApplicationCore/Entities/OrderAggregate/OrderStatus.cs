namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no payment or fulfilment state at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and the authorization captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the authorization was voided and funds released.</summary>
    Cancelled = 3
}
