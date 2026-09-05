using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed record ShopperProfile(string UserId, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(string Handle, string Name, int PriceInCents, int Interval, string IntervalUnit);

public sealed record BillingSubscription(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscriptionEnrollmentResult(BillingSubscription? Subscription, bool IsPending, string Reference);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionEnrollmentResult> SubscribeAsync(ShopperProfile shopper, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(ShopperProfile shopper, CancellationToken cancellationToken);
}

public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
