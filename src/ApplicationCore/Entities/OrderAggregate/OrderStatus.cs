namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// High-level lifecycle of an <see cref="Order"/>. Detailed money movement (hold, capture,
/// refunds) lives on the associated <see cref="Payment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed but not yet paid for. No money has been moved or held.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been authorized (held) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
