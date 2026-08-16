namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. Payment-specific state (PayPal ids,
/// capture/refund detail) lives on <see cref="Payment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled; the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold has been released.</summary>
    Cancelled = 3
}
