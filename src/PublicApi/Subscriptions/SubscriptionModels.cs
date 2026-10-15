using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    [Required, StringLength(255)] public string ProductHandle { get; set; } = "";
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
}

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents,
    int Interval, string IntervalUnit);
public record SubscriptionDetails(int Id, string ProductHandle, string PlanName, long PriceInCents,
    string? Currency, string State, DateTimeOffset? NextBillingDate, int? Interval, string? IntervalUnit);
public record SubscriptionEnrollment(string Status, SubscriptionDetails? Subscription);
public record MySubscriptions(IReadOnlyList<SubscriptionDetails> Subscriptions, bool HasPendingEnrollment);

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(string message, int statusCode = 503) : base(message) => StatusCode = statusCode;
}
