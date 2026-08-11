namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order with respect to its payment. This is additive state layered on top of
/// the classic eShopOnWeb order: an order created through the catalog checkout still starts life
/// <see cref="AwaitingPayment"/> and simply never leaves that state unless the payment flow drives it.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>PayPal is holding the funds (authorization). No money has moved.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds captured. Money has moved to the merchant.</summary>
    Fulfilled = 2,

    /// <summary>Authorization voided before fulfilment. No money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Fulfilled then part of the captured amount refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled then the whole captured amount refunded.</summary>
    Refunded = 5
}
