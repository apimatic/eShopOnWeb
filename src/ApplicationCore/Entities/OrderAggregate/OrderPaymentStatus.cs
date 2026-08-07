namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks where an order sits in the PayPal payment lifecycle. eShopOnWeb was originally
/// one-time commerce with no payment processing; this state was added to support paying for
/// and refunding an order.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>The order has been placed but not yet paid for.</summary>
    AwaitingPayment = 0,

    /// <summary>Payment was captured successfully via PayPal.</summary>
    Paid = 1,

    /// <summary>A captured payment was refunded in full via PayPal.</summary>
    Refunded = 2
}
