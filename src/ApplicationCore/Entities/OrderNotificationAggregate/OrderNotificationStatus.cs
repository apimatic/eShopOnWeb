namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

public enum OrderNotificationStatus
{
    Pending = 0,
    ProviderAccepted = 1,
    Failed = 2,
    CancellationPending = 3,
    Canceled = 4,
    OutcomeUnknown = 5,
    CancellationFailed = 6
}
