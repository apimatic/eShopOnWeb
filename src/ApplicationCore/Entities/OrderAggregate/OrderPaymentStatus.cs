namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Where the money for an order stands. Stored as text, so the names are part of the schema.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>No payment has been taken; the order can be paid.</summary>
    AwaitingPayment = 0,

    /// <summary>A payment is in flight, pending at the provider, or its outcome is not yet known.</summary>
    PaymentProcessing = 1,

    /// <summary>The order total has been charged.</summary>
    Paid = 2,

    /// <summary>Part of the charged amount has been given back.</summary>
    PartiallyRefunded = 3,

    /// <summary>The whole charged amount has been given back.</summary>
    Refunded = 4
}
