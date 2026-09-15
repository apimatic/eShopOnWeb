using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Coordinates local user ownership with Maxio, which remains authoritative for billing state.</summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly AppIdentityDbContext _identityContext;
    private readonly SubscriptionRequestLock _requestLock;

    public MaxioSubscriptionService(IMaxioClient maxio, AppIdentityDbContext identityContext, SubscriptionRequestLock requestLock)
    {
        _maxio = maxio;
        _identityContext = identityContext;
        _requestLock = requestLock;
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await FindKnownCustomerAsync(user, cancellationToken);
        if (customer is null) return [];

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        await SynchronizeLinksAsync(user.Id, subscriptions, cancellationToken);
        return subscriptions.Select(subscription => ToSummary(subscription, plans)).ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var semaphore = _requestLock.For(user.Id);
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var plans = await _maxio.ListPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(candidate =>
                !candidate.IsArchived && string.Equals(candidate.Handle, planHandle, StringComparison.Ordinal));
            if (plan is null) throw new UnknownSubscriptionPlanException(planHandle);

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var currentSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = currentSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.ProductHandle, plan.Handle, StringComparison.Ordinal) && IsCurrent(subscription.State));
            if (existing is not null)
            {
                await SynchronizeLinksAsync(user.Id, [existing], cancellationToken);
                return ToSummary(existing, plans);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                await SynchronizeLinksAsync(user.Id, [created], cancellationToken);
                return ToSummary(created, plans);
            }
            catch (MaxioApiException)
            {
                // A network failure after Maxio accepted the request is resolved by querying its system of record.
                var afterFailure = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var recovered = afterFailure.FirstOrDefault(subscription =>
                    string.Equals(subscription.ProductHandle, plan.Handle, StringComparison.Ordinal) && IsCurrent(subscription.State));
                if (recovered is null) throw;
                await SynchronizeLinksAsync(user.Id, [recovered], cancellationToken);
                return ToSummary(recovered, plans);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<MaxioCustomer?> FindKnownCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = GetCustomerReference(user);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null) await SaveCustomerMappingAsync(user, customer, cancellationToken);
        return customer;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await FindKnownCustomerAsync(user, cancellationToken);
        if (existing is not null) return existing;

        var reference = GetCustomerReference(user);
        try
        {
            var created = await _maxio.CreateCustomerAsync(new MaxioCustomerCreate(
                FirstName: GetFirstName(user), LastName: "Shopper", Email: user.Email ?? user.UserName ?? $"{user.Id}@invalid.local", Reference: reference), cancellationToken);
            await SaveCustomerMappingAsync(user, created, cancellationToken);
            return created;
        }
        catch (MaxioApiException exception) when ((int)exception.StatusCode == 422)
        {
            // The reference is unique in Maxio. A parallel request may have created it first.
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentCustomer is null) throw;
            await SaveCustomerMappingAsync(user, concurrentCustomer, cancellationToken);
            return concurrentCustomer;
        }
    }

    private async Task SaveCustomerMappingAsync(ApplicationUser user, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        if (user.MaxioCustomerId == customer.Id && user.MaxioCustomerReference == customer.Reference) return;
        user.MaxioCustomerId = customer.Id;
        user.MaxioCustomerReference = customer.Reference;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SynchronizeLinksAsync(string userId, IEnumerable<MaxioSubscription> subscriptions, CancellationToken cancellationToken)
    {
        var incoming = subscriptions.Where(subscription => !string.IsNullOrWhiteSpace(subscription.ProductHandle)).ToList();
        if (incoming.Count == 0) return;
        var knownIds = await _identityContext.MaxioSubscriptionLinks
            .Where(link => link.UserId == userId)
            .Select(link => link.MaxioSubscriptionId)
            .ToListAsync(cancellationToken);
        foreach (var subscription in incoming.Where(subscription => !knownIds.Contains(subscription.Id)))
        {
            _identityContext.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink
            {
                UserId = userId,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = subscription.ProductHandle!
            });
        }
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetCustomerReference(ApplicationUser user) => user.MaxioCustomerReference ?? $"eshoponweb:{user.Id}";
    private static string GetFirstName(ApplicationUser user)
    {
        var source = user.UserName ?? user.Email ?? "eShopOnWeb";
        var at = source.IndexOf('@');
        return at > 0 ? source[..at] : source;
    }
    private static bool IsCurrent(string state) => !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase);

    private static SubscriptionSummary ToSummary(MaxioSubscription subscription, IReadOnlyList<MaxioPlan> plans)
    {
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, subscription.ProductHandle, StringComparison.Ordinal));
        return new SubscriptionSummary(
            subscription.Id,
            subscription.ProductHandle ?? "catalog-independent",
            plan?.Name ?? subscription.ProductName ?? "Subscription",
            plan?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            plan?.Interval ?? subscription.ProductInterval ?? 0,
            plan?.IntervalUnit ?? subscription.ProductIntervalUnit ?? "",
            subscription.State,
            subscription.NextBillingAt ?? subscription.CurrentPeriodEndsAt);
    }
}

public sealed class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle) : base($"The subscription plan '{planHandle}' is not available.") { }
}
