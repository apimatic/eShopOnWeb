namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order placed through the API: payment is authorized (held) at checkout,
/// captured when an operator fulfils the order, released on cancel.
/// </summary>
public enum OrderStatus
{
    PendingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3
}
