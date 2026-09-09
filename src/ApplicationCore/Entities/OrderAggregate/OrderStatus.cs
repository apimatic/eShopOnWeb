namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Coarse lifecycle of an <see cref="Order"/>. Payment-processor detail (hold/capture/refund ids
/// and their PayPal status) lives on the associated <see cref="Payment"/>; this reflects the
/// operational state an eShop operator or shopper reasons about.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet. The starting state.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal but not yet captured.</summary>
    PaymentAuthorized = 1,

    /// <summary>Order fulfilled and payment captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3
}
