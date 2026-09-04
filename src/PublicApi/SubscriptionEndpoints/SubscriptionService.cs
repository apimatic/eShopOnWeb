using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
    Task<SubscriptionResult> SubscribeAsync(ApplicationUser user, string? requestedProductHandle, CancellationToken cancellationToken);
}

public sealed record SubscriptionDetails(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionResult(SubscriptionDetails Subscription, bool Created);

public sealed class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshoponweb-user:";
    private const string SubscriptionReferencePrefix = "eshoponweb-subscription:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;

    public SubscriptionService(IMaxioBillingClient maxio, AppIdentityDbContext identityDb)
    {
        _maxio = maxio;
        _identityDb = identityDb;
    }

    public async Task<SubscriptionResult> SubscribeAsync(ApplicationUser user, string? requestedProductHandle, CancellationToken cancellationToken)
    {
        var userLock = UserLocks.GetOrAdd(user.Id, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var subscriptionReference = GetSubscriptionReference(user.Id);
            var existingMapping = await _identityDb.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(mapping => mapping.ApplicationUserId == user.Id, cancellationToken);

            var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                EnsureRequestedPlanMatches(existing, requestedProductHandle);
                if (existingMapping is null)
                {
                    var customer = await EnsureCustomerAsync(user, cancellationToken);
                    await SaveMappingAsync(user.Id, customer.Id, existing, cancellationToken);
                }

                return new SubscriptionResult(ToDetails(existing), false);
            }

            var plans = await _maxio.GetPlansAsync(cancellationToken);
            var plan = SelectPlan(plans, requestedProductHandle);
            var customerRecord = await EnsureCustomerAsync(user, cancellationToken);
            var paymentCollectionMethod = await _maxio.GetNoPaymentCollectionMethodAsync(cancellationToken);
            MaxioSubscriptionRecord created;
            try
            {
                created = await _maxio.CreateSubscriptionAsync(
                    customerRecord.Reference,
                    subscriptionReference,
                    plan.Handle,
                    paymentCollectionMethod,
                    GetNextBillingAt(plan),
                    cancellationToken);
            }
            catch (MaxioApiException)
            {
                // A timed-out response or a concurrent caller may have completed the
                // create. The deterministic reference makes the recovery idempotent.
                var recovered = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is null)
                {
                    throw;
                }

                EnsureRequestedPlanMatches(recovered, plan.Handle);
                await SaveMappingAsync(user.Id, customerRecord.Id, recovered, cancellationToken);
                return new SubscriptionResult(ToDetails(recovered), false);
            }

            await SaveMappingAsync(user.Id, customerRecord.Id, created, cancellationToken);
            return new SubscriptionResult(ToDetails(created), true);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(GetCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDetails).ToArray();
    }

    private async Task<MaxioCustomerRecord> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = GetCustomerReference(user.Id);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName ?? reference : user.Email;
        var firstName = GetCustomerFirstName(email);
        try
        {
            return await _maxio.CreateCustomerAsync(reference, firstName, "Customer", email, cancellationToken);
        }
        catch (MaxioApiException)
        {
            // Customer references are unique in Billing API. Recover a concurrent
            // successful create without ever creating a second customer.
            var recovered = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task SaveMappingAsync(string userId, int customerId, MaxioSubscriptionRecord subscription, CancellationToken cancellationToken)
    {
        var mapping = await _identityDb.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.ApplicationUserId == userId, cancellationToken);
        if (mapping is null)
        {
            mapping = new MaxioSubscriptionMapping
            {
                ApplicationUserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _identityDb.MaxioSubscriptionMappings.Add(mapping);
        }

        mapping.MaxioCustomerId = customerId;
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.SubscriptionReference = subscription.Reference ?? GetSubscriptionReference(userId);
        mapping.ProductHandle = subscription.ProductHandle ?? string.Empty;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static MaxioPlan SelectPlan(IReadOnlyList<MaxioPlan> plans, string? requestedProductHandle)
    {
        if (!string.IsNullOrWhiteSpace(requestedProductHandle))
        {
            var requested = plans.FirstOrDefault(plan => string.Equals(plan.Handle, requestedProductHandle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (requested is null)
            {
                throw new SubscriptionConflictException("The requested subscription plan is not available.");
            }

            return requested;
        }

        return plans.FirstOrDefault(plan =>
                plan.Name.Contains("pro", StringComparison.OrdinalIgnoreCase) ||
                plan.Handle.Contains("pro", StringComparison.OrdinalIgnoreCase))
            ?? plans.FirstOrDefault()
            ?? throw new SubscriptionConflictException("No subscription plans are available.");
    }

    private static void EnsureRequestedPlanMatches(MaxioSubscriptionRecord existing, string? requestedProductHandle)
    {
        if (!string.IsNullOrWhiteSpace(requestedProductHandle) &&
            !string.Equals(existing.ProductHandle, requestedProductHandle.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionConflictException("The account already has a subscription on a different plan.");
        }
    }

    private static SubscriptionDetails ToDetails(MaxioSubscriptionRecord subscription)
    {
        return new SubscriptionDetails(
            subscription.Id,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.State,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string GetCustomerReference(string userId) => CustomerReferencePrefix + userId;
    private static string GetSubscriptionReference(string userId) => SubscriptionReferencePrefix + userId;

    private static DateTimeOffset GetNextBillingAt(MaxioPlan plan)
    {
        var now = DateTimeOffset.UtcNow;
        return plan.IntervalUnit.Equals("month", StringComparison.OrdinalIgnoreCase)
            ? now.AddMonths(plan.Interval)
            : now.AddDays(plan.Interval);
    }

    private static string GetCustomerFirstName(string email)
    {
        var localPart = email.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
    }
}
