namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum RefundStatus
{
    /// <summary>Recorded before the provider was called; the outcome is not known yet.</summary>
    Initiated = 0,
    /// <summary>The provider accepted the refund request.</summary>
    Received = 1,
    /// <summary>The provider rejected the refund request; no money was returned.</summary>
    Failed = 2,
    /// <summary>The provider did not answer; the refund may or may not have been accepted.</summary>
    Unknown = 3
}
