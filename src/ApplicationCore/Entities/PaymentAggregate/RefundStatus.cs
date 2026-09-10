namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>State of an individual refund against a captured payment.</summary>
public enum RefundStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2
}
