namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where the money for an order stands. Derived from the order's payment attempts and refunds
/// (see <see cref="Order.RecomputePaymentStatus"/>) and persisted with the order.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>No money has been taken; the shopper can pay.</summary>
    AwaitingPayment = 0,

    /// <summary>A payment is in flight, or its outcome is not yet known. No new payment may start.</summary>
    PaymentPending = 1,

    /// <summary>The order total has been taken.</summary>
    Paid = 2,

    /// <summary>Part of the money taken has been given back.</summary>
    PartiallyRefunded = 3,

    /// <summary>All of the money taken has been given back.</summary>
    Refunded = 4
}
