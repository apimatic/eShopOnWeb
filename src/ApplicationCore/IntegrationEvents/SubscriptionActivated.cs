using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process, best-effort, once a customer has been successfully enrolled in a plan (UC1).
/// </summary>
public record SubscriptionActivated(
    string BuyerId,
    int BillingSubscriptionId,
    string PlanHandle,
    decimal PlanPrice) : INotification;
