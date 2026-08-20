using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingPlan(long Id, string Handle, string Name, string Description, long PriceInCents,
    int Interval, string IntervalUnit);

public sealed record BillingCustomer(long Id, string Reference);

public sealed record BillingSubscription(long Id, long CustomerId, string? Reference, string State,
    string ProductHandle, string ProductName, long PriceInCents, int Interval, string IntervalUnit,
    DateTimeOffset? NextBillingAt, string ProductFamilyHandle);

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

public sealed record SubscriptionResult(BillingSubscription Subscription, bool Created);

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);
    Task<BillingCustomer> CreateCustomerAsync(BillingUser user, string reference,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default);
    Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionResult> SubscribeAsync(BillingUser user, string productHandle,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(BillingUser user,
        CancellationToken cancellationToken = default);
}

public sealed class SubscriptionProvisioningInProgressException : Exception
{
    public SubscriptionProvisioningInProgressException()
        : base("A subscription request for this plan is already being processed.") { }
}

public sealed class BillingPlanNotFoundException : Exception
{
    public BillingPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.") { }
}
