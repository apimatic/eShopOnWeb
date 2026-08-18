namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an <see cref="Order"/>. An order is additive to the existing catalog
/// flow: it starts <see cref="AwaitingPayment"/> and moves through authorization, capture (at fulfilment) and
/// return.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed; no hold on money yet.</summary>
    AwaitingPayment,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized,

    /// <summary>Order fulfilled; money captured.</summary>
    Fulfilled,

    /// <summary>Cancelled before fulfilment; the authorization was voided and no money moved.</summary>
    Cancelled,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded
}
