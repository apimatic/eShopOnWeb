namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment / fulfilment lifecycle state of an <see cref="Order"/>.
/// eShopOnWeb originally ended checkout by writing an Order row with no payment state;
/// this enum adds the money-movement lifecycle on top of the existing order model.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (a hold is in place) but not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds have been captured.</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any hold was released, no money moved.</summary>
    Cancelled = 3,

    /// <summary>Fully refunded after fulfilment.</summary>
    Refunded = 4,

    /// <summary>Partially refunded after fulfilment.</summary>
    PartiallyRefunded = 5
}
