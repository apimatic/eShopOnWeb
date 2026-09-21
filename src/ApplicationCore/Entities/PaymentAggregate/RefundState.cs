namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The state of a single <see cref="PaymentRefund"/> as last reported by PayPal.
/// </summary>
public enum RefundState
{
    /// <summary>The refund has been requested locally but PayPal has not yet confirmed a terminal state.</summary>
    Pending = 0,

    /// <summary>PayPal reported the refund as completed.</summary>
    Completed = 1,

    /// <summary>PayPal reported the refund as failed.</summary>
    Failed = 2,

    /// <summary>PayPal reported the refund as cancelled.</summary>
    Cancelled = 3
}
