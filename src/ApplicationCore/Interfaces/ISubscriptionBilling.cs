using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description,
    long PriceInCents, int Interval, string IntervalUnit);

public record ShopperSubscription(int Id, string PlanHandle, string PlanName, long PriceInCents,
    string Currency, string State, DateTimeOffset? NextBillingDate, int Interval, string IntervalUnit);

public record BillingPage<T>(IReadOnlyList<T> Items, bool IsPartial = false, int? NextPage = null);
public record EnrollmentResult(string Status, ShopperSubscription? Subscription, bool Created = false);
public record AccountSubscriptions(BillingPage<ShopperSubscription> Subscriptions, bool HasPendingEnrollment);

public interface ISubscriptionBilling
{
    Task<BillingPage<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<EnrollmentResult> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken);
    Task<AccountSubscriptions> GetMineAsync(string userId, CancellationToken cancellationToken);
    Task ReconcileAsync(CancellationToken cancellationToken);
}

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
