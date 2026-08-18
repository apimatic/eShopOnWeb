namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. Additive to the existing Order aggregate — an Order
/// row is created awaiting payment, then moves through these states as the shopper pays and
/// an operator fulfils, cancels or refunds it.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) at PayPal; not captured.</summary>
    Authorized = 1,

    /// <summary>Authorization captured at fulfilment; money taken.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
