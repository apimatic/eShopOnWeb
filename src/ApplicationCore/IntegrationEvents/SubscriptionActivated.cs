using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer has been successfully enrolled in a plan (UC1).
/// <para>
/// Delivery is best-effort: eShopOnWeb has no broker and no outbox, so a handler that throws is
/// logged and the subscription still stands (plan.md §2.5).
/// </para>
/// </summary>
/// <param name="SubscriptionId">The provider's identifier for the new subscription.</param>
/// <param name="UserReference">The eShopOnWeb user (email/username) the subscription belongs to.</param>
/// <param name="PlanHandle">The stable handle of the plan enrolled in.</param>
/// <param name="PlanName">The display name of the plan enrolled in.</param>
/// <param name="Price">The recurring price in whole currency units.</param>
/// <param name="NextBillingAt">When the subscription will next be billed, if known.</param>
public record SubscriptionActivated(
    int SubscriptionId,
    string UserReference,
    string PlanHandle,
    string? PlanName,
    decimal Price,
    DateTimeOffset? NextBillingAt) : INotification;
