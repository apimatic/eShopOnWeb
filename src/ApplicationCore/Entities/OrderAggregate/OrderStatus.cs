namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order is in its lifecycle. Added so an order can be marked dispatched or cancelled and
/// so notifications can follow it as it moves. Additive to the existing order model.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
