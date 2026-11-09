namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentState
{
    Unpaid = 0,
    /// <summary>Submitted to Stax, waiting for the bank to settle it.</summary>
    Pending = 1,
    Paid = 2,
    Failed = 3
}

public enum RefundStatus
{
    None = 0,
    /// <summary>Requested by staff; will be completed automatically once Stax can carry it out.</summary>
    Pending = 1,
    Refunded = 2
}
