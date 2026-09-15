namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}

public enum NotificationSubmissionStatus
{
    Pending,
    Accepted,
    Rejected,
    Ambiguous,
    Skipped
}

public enum ProviderActionState
{
    None,
    Pending,
    Confirmed
}
