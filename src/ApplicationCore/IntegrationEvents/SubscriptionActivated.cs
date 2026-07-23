using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a customer is successfully enrolled in a plan (plan.md UC1, step 6).
/// </summary>
/// <remarks>
/// Delivery is best-effort and in-process only — eShopOnWeb has no broker or outbox (plan.md §2.5).
/// A handler failure never rolls back the enrolment.
/// </remarks>
public sealed record SubscriptionActivated(
    int SubscriptionId,
    string CustomerReference,
    string PlanHandle,
    string? PlanName,
    decimal PlanPrice,
    DateTimeOffset? NextBillingDate) : INotification;
