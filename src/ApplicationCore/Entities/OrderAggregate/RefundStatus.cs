namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum RefundStatus
{
    // Claimed (amount reserved) and sent (or about to be sent) to the payment provider.
    Requested = 0,
    // The provider accepted the refund request; it settles asynchronously.
    Received = 1,
    // The provider rejected the refund, or it would exceed the refundable amount. Nothing stays reserved.
    Failed = 2,
    // The provider may have acted but its answer could not be read. The amount stays reserved until settled.
    Unknown = 3
}
