namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its fulfilment lifecycle. Before SMS notifications the app had no notion
/// of an order having been dispatched or cancelled; this captures that.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but not yet dispatched.</summary>
    Submitted = 0,

    /// <summary>An operator has marked the order dispatched.</summary>
    Dispatched = 1,

    /// <summary>An operator has cancelled the order.</summary>
    Cancelled = 2
}
