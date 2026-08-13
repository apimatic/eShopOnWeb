namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle state of an <see cref="Order"/>. Added additively to support the SMS
/// notification flow (dispatch / cancel) without altering the existing checkout model.
/// </summary>
public enum OrderStatus
{
    Submitted = 0,
    Dispatched = 1,
    Cancelled = 2
}
