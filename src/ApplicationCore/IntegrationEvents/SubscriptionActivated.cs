using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announced in-process after a customer has been successfully enrolled in a plan (UC1 step 6).
/// Publication is best-effort: a handler failure never rolls back the enrolment.
/// </summary>
public record SubscriptionActivated(
    int SubscriptionId,
    string UserName,
    string PlanHandle,
    string PlanName,
    decimal PlanPrice,
    DateTimeOffset? NextBillingDate) : INotification;
