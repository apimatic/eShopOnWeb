using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int SubscriptionId,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    int Interval,
    string IntervalUnit);

public sealed class CreateSubscriptionRequest
{
    [Required]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record CreateSubscriptionResponse(
    string Status,
    SubscriptionDto? Subscription,
    string? Message = null);

public sealed record EnrollmentResult(bool IsPending, SubscriptionDto? Subscription)
{
    public static EnrollmentResult Completed(SubscriptionDto subscription) => new(false, subscription);
    public static EnrollmentResult Pending() => new(true, null);
}

internal sealed record BillingCustomerIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    string CustomerReference);

internal sealed record MaxioCustomer(int Id);
