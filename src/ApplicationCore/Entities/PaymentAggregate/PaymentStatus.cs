namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Where the payment for an order stands.</summary>
public enum PaymentStatus
{
    /// <summary>A charge is being made right now.</summary>
    Processing = 1,

    /// <summary>The bank payment was accepted and is waiting for the bank to settle it.</summary>
    PendingSettlement = 2,

    /// <summary>The payment has gone through.</summary>
    Paid = 3,

    /// <summary>The payment was declined or returned.</summary>
    Failed = 4
}

/// <summary>Where the refund of an order stands.</summary>
public enum RefundStatus
{
    None = 0,

    /// <summary>Staff asked for the refund and the shop completes it without further action (banks only allow ACH refunds days after the payment).</summary>
    Pending = 1,

    /// <summary>The whole amount went back to the shopper.</summary>
    Refunded = 2
}
