using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record BillingShopper(string UserId, string Email);
public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents,
    int Interval, string IntervalUnit, bool? RequiresPaymentMethod);
public record SubscriptionPlans(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);
public record ShopperSubscription(int Id, string PlanHandle, string? PlanName, long? PriceInCents,
    string? Currency, int? Interval, string? IntervalUnit, string? State,
    DateTimeOffset? NextBillingDate, DateTimeOffset? CurrentPeriodEndsAt);
public record SubscribeResult(ShopperSubscription? Subscription, bool IsPending);
public record MySubscriptions(IReadOnlyList<ShopperSubscription> Subscriptions, bool HasPendingRequests);

public interface ISubscriptionBilling
{
    Task<SubscriptionPlans> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string productHandle, CancellationToken cancellationToken);
    Task<MySubscriptions> GetMineAsync(BillingShopper shopper, CancellationToken cancellationToken);
}

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
