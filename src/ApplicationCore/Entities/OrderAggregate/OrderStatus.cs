namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order from placement through payment to fulfilment.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no successful authorization exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is authorized (funds on hold) at the payment processor.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order is fulfilled and the payment captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
