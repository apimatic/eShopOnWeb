using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents,
    int Interval, string IntervalUnit, string? Currency = null);
public record SubscriptionDetails(int Id, string PlanHandle, string PlanName, long PriceInCents,
    string? Currency, int Interval, string IntervalUnit, string State, DateTimeOffset? NextBillingDate);
public record SubscriptionEnrollment(string Status, SubscriptionDetails? Subscription);
public record SubscriptionAccount(IReadOnlyList<SubscriptionDetails> Subscriptions, bool HasPendingEnrollment);

public interface ISubscriptionBilling
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken);
    Task<SubscriptionAccount> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
