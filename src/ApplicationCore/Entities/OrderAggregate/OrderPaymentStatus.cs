namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks where an order sits in the payment lifecycle. This is additive to the
/// existing order flow: an order created without a payment simply stays in
/// <see cref="AwaitingPayment"/>.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed but no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held at PayPal (authorization created) but not captured.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and the held funds were captured (money taken).</summary>
    Captured = 2,

    /// <summary>Authorization voided before fulfilment; no money ever moved.</summary>
    Cancelled = 3,

    /// <summary>Captured payment fully returned to the shopper.</summary>
    Refunded = 4,

    /// <summary>Part of the captured payment has been returned to the shopper.</summary>
    PartiallyRefunded = 5
}
