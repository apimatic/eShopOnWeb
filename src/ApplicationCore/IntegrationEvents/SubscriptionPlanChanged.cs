using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces that a subscription moved between plans. Published best effort and in-process only
/// (plan §2.5).
/// </summary>
public record SubscriptionPlanChanged(
    string UserReference,
    int SubscriptionId,
    string PreviousPlanHandle,
    string NewPlanHandle,
    PlanChangeTiming Timing,
    decimal AppliedPaymentDue) : INotification;
