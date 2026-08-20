using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingCustomer(
    long Id,
    string Reference,
    string Email);

public sealed record BillingSubscription(
    long Id,
    string? Reference,
    string State,
    long PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    long CustomerId,
    string? CustomerReference,
    string ProductHandle,
    string ProductName,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle);

public sealed record SubscriptionDetails(
    long Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt);

public sealed record SubscribeResult(SubscriptionDetails Subscription, bool Created);

public sealed record CurrentUser(string Id, string UserName, string Email);

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken);
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

public interface ICurrentUserService
{
    Task<CurrentUser> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public interface ISubscriptionEnrollmentLock
{
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken);
}
