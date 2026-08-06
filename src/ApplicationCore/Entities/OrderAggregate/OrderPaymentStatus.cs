namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks where an order sits in the (additive) PayPal payment lifecycle.
/// The existing one-time checkout flow does not touch this; it only matters for
/// orders driven through the payments API.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Order placed but not yet paid.</summary>
    AwaitingPayment = 0,

    /// <summary>Payment captured successfully through PayPal.</summary>
    Paid = 1,

    /// <summary>A previously captured payment has been refunded in full.</summary>
    Refunded = 2
}
