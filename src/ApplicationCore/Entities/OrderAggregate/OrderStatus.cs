namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The fulfilment lifecycle of an <see cref="Order"/>. The money state that PayPal owns
/// (authorization / capture / refunds) is tracked separately on the <see cref="Payment"/>.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order has been placed but no payment hold exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are on hold (authorized) but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>The order has been fulfilled and the held funds captured.</summary>
    Fulfilled = 2,

    /// <summary>The order was cancelled before fulfilment and the hold was released.</summary>
    Cancelled = 3
}
