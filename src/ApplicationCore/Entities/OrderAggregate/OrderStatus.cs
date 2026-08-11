namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an order once payment is involved.
/// Additive to the classic checkout flow: a classic order simply stays <see cref="AwaitingPayment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with PayPal, awaiting fulfilment.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
