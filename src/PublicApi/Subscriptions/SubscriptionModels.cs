using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string PricePointName);

public sealed record SubscriptionDto(
    long Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string PricePointName,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record MaxioCustomer(long Id, string Reference);

public sealed record MaxioCustomerInput(
    string FirstName,
    string LastName,
    string Email,
    string Reference);

public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? providerMessage = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    public int StatusCode { get; }
    public string? ProviderMessage { get; }
}

public class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message) { }
}

public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message) { }
}
