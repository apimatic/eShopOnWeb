namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment lifecycle of an <see cref="Order"/>. This is an additive capability layered on top of
/// the existing one-time order model; it does not change how orders are placed.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>The order has been placed but not yet paid.</summary>
    AwaitingPayment = 0,

    /// <summary>The order's payment has been captured successfully.</summary>
    Paid = 1,

    /// <summary>The captured payment has been fully refunded.</summary>
    Refunded = 2,

    /// <summary>The last payment attempt failed; the order can be retried.</summary>
    Failed = 3
}
