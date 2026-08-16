namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Fulfilment lifecycle of an <see cref="Order"/>. This is additive to the original
/// one-time-commerce flow: an order now starts awaiting payment and moves forward as the
/// shopper pays and an operator fulfils, cancels or refunds it.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed but not yet paid; no money has been held.</summary>
    AwaitingPayment = 0,

    /// <summary>Payment authorized: funds are held with PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Operator fulfilled the order and the held funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>Order cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
