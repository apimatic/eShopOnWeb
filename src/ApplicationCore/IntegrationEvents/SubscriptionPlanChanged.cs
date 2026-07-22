using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, once a subscription has moved to a different plan (UC3).
/// </summary>
public record SubscriptionPlanChanged(
    int BillingSubscriptionId,
    string OldPlanHandle,
    string NewPlanHandle,
    decimal PaymentDue) : INotification;
