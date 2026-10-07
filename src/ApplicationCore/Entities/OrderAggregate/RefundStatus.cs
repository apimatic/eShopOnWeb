namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum RefundStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the provider; no answer recorded yet. The amount is reserved.</summary>
    InFlight = 0,

    /// <summary>The provider accepted the refund request; the money is on its way back to the shopper.</summary>
    Received = 1,

    /// <summary>The provider rejected the refund request; the amount is released.</summary>
    Rejected = 2,

    /// <summary>The provider did not answer; the amount stays reserved until the refund is settled.</summary>
    Unknown = 3
}
