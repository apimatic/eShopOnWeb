namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The lifecycle of the money movement for an order. This is the payment/fulfilment state that the
/// existing <c>Order</c> aggregate never carried.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Order fulfilled and money captured.</summary>
    Captured = 2,

    /// <summary>Authorization voided before fulfilment; no money ever moved.</summary>
    Canceled = 3,

    /// <summary>Captured payment refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>Captured payment refunded in full.</summary>
    Refunded = 5
}
