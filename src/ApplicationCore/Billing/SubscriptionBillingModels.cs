using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionSubscriber(string UserId, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionSummary(
    string Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
