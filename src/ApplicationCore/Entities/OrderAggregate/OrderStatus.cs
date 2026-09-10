namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no status at all.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Fulfilled by an operator; the authorization has been captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds have been released.</summary>
    Cancelled = 3
}
