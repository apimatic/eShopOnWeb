using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, once a lifecycle transition has been applied (UC4).
/// </summary>
public record SubscriptionStateChanged(
    int BillingSubscriptionId,
    SubscriptionLifecycleAction Action,
    string OldState,
    string NewState) : INotification;
