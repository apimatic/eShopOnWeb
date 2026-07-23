using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the proration basis moved between the preview the customer was shown and the commit
/// they confirmed. The plan change is refused so that the customer is never charged an amount other
/// than the one they saw; the caller must take a fresh preview and confirm again (UC3).
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(int subscriptionId, long confirmedPaymentDueInCents, long currentPaymentDueInCents)
        : base($"The previewed cost for subscription {subscriptionId} is no longer current " +
               $"(confirmed {confirmedPaymentDueInCents} cents, provider now quotes {currentPaymentDueInCents} cents). " +
               "Take a fresh preview and confirm again.")
    {
        SubscriptionId = subscriptionId;
        ConfirmedPaymentDueInCents = confirmedPaymentDueInCents;
        CurrentPaymentDueInCents = currentPaymentDueInCents;
    }

    public int SubscriptionId { get; }

    public long ConfirmedPaymentDueInCents { get; }

    public long CurrentPaymentDueInCents { get; }
}
