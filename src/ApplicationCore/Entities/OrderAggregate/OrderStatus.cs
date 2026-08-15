namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an <see cref="Order"/> as money moves through PayPal.
/// Refund state is tracked on the <see cref="Payment"/> (captured vs refunded amounts)
/// rather than as a distinct order status.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed; total not yet authorized.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been held (authorized) but not captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds released.</summary>
    Cancelled = 3
}
