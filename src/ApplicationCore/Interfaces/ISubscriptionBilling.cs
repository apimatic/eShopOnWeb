using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record BillingShopper(string UserId, string NormalizedUsername, string Email);
public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents, int Interval, string IntervalUnit, bool RequiresPaymentMethod, string PaymentCollectionMethod);
public record SubscriptionPlans(IReadOnlyList<SubscriptionPlan> Plans, bool IsPartial, int? NextPage);
public record ShopperSubscription(int Id, string ProductHandle, string Plan, long PriceInCents, string? Currency, string State, DateTimeOffset? NextBillingDate, string? NextBillingDateSource, string? PaymentCollectionMethod);
public record SubscriptionEnrollment(string Status, ShopperSubscription? Subscription, bool Created = false);
public record ShopperSubscriptions(IReadOnlyList<ShopperSubscription> Subscriptions, bool HasPendingEnrollment);

public interface ISubscriptionBilling
{
    Task<SubscriptionPlans> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionEnrollment> SubscribeAsync(BillingShopper shopper, string productHandle, string firstName, string lastName, CancellationToken cancellationToken);
    Task<ShopperSubscriptions> ListMineAsync(BillingShopper shopper, CancellationToken cancellationToken);
}

public sealed class BillingException : Exception
{
    public int StatusCode { get; }
    public BillingException(string safeMessage, int statusCode = 502) : base(safeMessage) => StatusCode = statusCode;
}
