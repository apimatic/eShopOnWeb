namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// eShopOnWeb order model, which had no status at all. Money movement detail lives on
/// the associated <see cref="Payment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (PayPal authorization) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
