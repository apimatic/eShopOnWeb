using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscribeRequest(
    [Required, StringLength(100)] string ProductHandle,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName);

public sealed record SubscriptionPlan(string Handle, string Name, string? Description,
    long PriceInCents, int Interval, string IntervalUnit, bool RequiresPaymentMethod);
public sealed record PlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);
public sealed record SubscriptionView(int Id, string ProductHandle, string PlanName,
    long PriceInCents, string Currency, string State, DateTimeOffset? NextBillingDate);
public sealed record SubscribeResult(string Status, SubscriptionView? Subscription);

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(int status, string message) : base(message) => StatusCode = status;
}
