namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the money for an order, as reported by the payment processor.
/// </summary>
public enum PaymentStatus
{
    /// <summary>No money movement has been started for the order yet.</summary>
    AwaitingPayment = 0,

    /// <summary>The order total is on hold; it has not been taken.</summary>
    Authorized = 1,

    /// <summary>The held money has been taken in full.</summary>
    Captured = 2,

    /// <summary>The hold was released before it was ever taken; no money moved.</summary>
    Voided = 3,

    /// <summary>Part of the captured money has been returned to the shopper.</summary>
    PartiallyRefunded = 4,

    /// <summary>All of the captured money has been returned to the shopper.</summary>
    FullyRefunded = 5,

    /// <summary>The processor refused the card; the order can be paid for again.</summary>
    Declined = 6,

    /// <summary>The order was called off while no money was on hold.</summary>
    Cancelled = 7
}

/// <summary>
/// State of a single refund.
/// </summary>
public enum RefundStatus
{
    /// <summary>The refund has been recorded and accepted by the processor.</summary>
    Completed = 0,

    /// <summary>The processor is still settling the refund.</summary>
    Pending = 1,

    /// <summary>The processor refused the refund.</summary>
    Failed = 2
}
