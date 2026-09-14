using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record BillingUser(string Id, string Email, string UserName);

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    long Id,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' is not available in the configured product family.")
    {
    }
}

public sealed class SubscriptionPaymentMethodRequiredException : Exception
{
    public SubscriptionPaymentMethodRequiredException(string productHandle)
        : base($"Subscription plan '{productHandle}' requires a payment method and cannot be purchased through this no-card endpoint.")
    {
    }
}
