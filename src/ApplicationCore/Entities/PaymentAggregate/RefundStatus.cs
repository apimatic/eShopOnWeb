namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum RefundStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the payment provider.</summary>
    Processing,
    /// <summary>The provider accepted the refund request.</summary>
    Received,
    /// <summary>The provider rejected the refund request. Nothing was refunded.</summary>
    Rejected,
    /// <summary>The call to the provider failed in a way that leaves the outcome unknown; it must be settled.</summary>
    Unknown
}
