namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// Lifecycle of an order's payment. Additive to the existing order flow: an order
/// begins <see cref="PendingPayment"/> and moves through the PayPal money movement.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Order placed, no money held yet. Awaiting authorization.</summary>
    PendingPayment = 0,

    /// <summary>Funds held with PayPal (authorization) but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Funds captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>Captured, then part of the amount returned to the shopper.</summary>
    PartiallyRefunded = 3,

    /// <summary>Captured, then the whole captured amount returned to the shopper.</summary>
    Refunded = 4,

    /// <summary>Authorization released before capture; no money ever moved.</summary>
    Cancelled = 5,

    /// <summary>The payment attempt failed and no hold survives.</summary>
    Failed = 6
}
