namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/> as money moves through PayPal.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no payment hold exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) with PayPal but not yet captured.</summary>
    Authorized = 1,

    /// <summary>The order was fulfilled and the held funds were captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; the hold was released.</summary>
    Cancelled = 3,

    /// <summary>A captured payment was refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>A captured payment was refunded in full.</summary>
    Refunded = 5
}
