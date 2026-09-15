namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment/payment lifecycle of an <see cref="Order"/>. eShopOnWeb originally ended
/// checkout by writing an order row with no payment state; these states model the money
/// movement that a real payment adds on top of that flow.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
