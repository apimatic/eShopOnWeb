using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record BillingCustomerProfile(
    string StableUserId,
    string Email,
    string FirstName,
    string LastName);

public sealed record BillingCustomer(int Id, string Reference);

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken);
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(
        string productHandle,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(CancellationToken cancellationToken);
}
