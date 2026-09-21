namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment/fulfilment lifecycle of an order. This state is additive to the existing
/// <see cref="OrderAggregate.Order"/> aggregate (which itself carries no payment state).
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed; no money has been held yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are held (authorized) at PayPal but not yet taken.</summary>
    Authorized = 1,

    /// <summary>Operator fulfilled the order; the held funds were captured (taken).</summary>
    Fulfilled = 2,

    /// <summary>Cancelled before fulfilment; the held funds were released. No money moved.</summary>
    Canceled = 3,

    /// <summary>Fulfilled and then partly refunded.</summary>
    PartiallyRefunded = 4,

    /// <summary>Fulfilled and then fully refunded.</summary>
    Refunded = 5,

    /// <summary>A payment operation failed in a way that needs operator attention.</summary>
    Failed = 6
}
