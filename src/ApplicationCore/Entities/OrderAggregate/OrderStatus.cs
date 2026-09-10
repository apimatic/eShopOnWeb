namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to payment and fulfilment.
/// The existing catalog/basket flow does not use this; it is part of the additive
/// payment capability. An order created through the payment API starts life
/// <see cref="AwaitingPayment"/> and moves forward only through the payment endpoints.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds authorized (held) with PayPal, not yet captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the authorized funds captured (money taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; any held funds were released.</summary>
    Cancelled = 3,

    /// <summary>Captured payment partially returned to the shopper.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment fully returned to the shopper.</summary>
    Refunded = 5
}
