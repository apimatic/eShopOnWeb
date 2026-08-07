namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Lifecycle of an order's payment. eShopOnWeb is one-time commerce, so an order is created
/// awaiting payment, becomes paid once PayPal captures the funds, and can later be refunded in full.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>The order has been placed but no successful payment has been captured yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds have been captured by PayPal for this order.</summary>
    Paid = 1,

    /// <summary>The captured payment has been refunded in full.</summary>
    Refunded = 2
}
