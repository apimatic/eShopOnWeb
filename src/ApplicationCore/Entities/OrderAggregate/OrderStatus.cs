namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where an order sits in the fulfilment flow this feature tracks. Additive to the existing
/// order model — an order starts <see cref="Placed"/> and an operator moves it on from there.
/// </summary>
public enum OrderStatus
{
    Placed = 1,
    Dispatched = 2,
    Cancelled = 3
}
