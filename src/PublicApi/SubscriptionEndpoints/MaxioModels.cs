using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record MaxioCustomer(long Id);

public sealed record MaxioSubscription(
    long Id,
    string Reference,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string operation)
        : base($"Maxio rejected {operation} with HTTP {statusCode}.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
