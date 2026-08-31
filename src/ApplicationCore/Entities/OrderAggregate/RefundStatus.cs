namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum RefundStatus
{
    /// <summary>Recorded locally, not yet confirmed by PayPal.</summary>
    Pending = 0,
    Completed = 1,
    Failed = 2
}
