namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to its PayPal payment.
/// Refund state is derived from <see cref="OrderPayment"/> (see RefundedAmount / RefundableRemaining).
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Fulfilled: the authorization has been captured and money taken.</summary>
    Paid = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
