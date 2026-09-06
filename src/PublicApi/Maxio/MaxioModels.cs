using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod,
    string? Currency);

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    int Id,
    string Reference,
    string State,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    DateTimeOffset? NextBillingAt,
    int CustomerId);

public sealed record EnrollmentResult(MaxioSubscription Subscription, bool Created);

public sealed class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException() : base("The subscription enrollment is still being processed. Retry shortly using the same plan.") { }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode) : base("Maxio Billing API rejected the request.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
