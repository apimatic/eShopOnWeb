namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The lifecycle state of an <see cref="Order"/>. eShopOnWeb historically had no notion of an order
/// progressing after checkout; this adds the minimal set of transitions the SMS notification flow needs.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed by the shopper and is awaiting fulfilment.</summary>
    Placed = 0,

    /// <summary>An operator has marked the order as dispatched / on its way to the shopper.</summary>
    Dispatched = 1,

    /// <summary>An operator has cancelled the order.</summary>
    Cancelled = 2
}
