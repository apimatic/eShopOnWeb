namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentRefundStatus
{
    /// <summary>Sent to the provider; outcome unknown or the provider reported it as pending.</summary>
    Pending = 0,
    Completed = 1,
    Failed = 2
}
