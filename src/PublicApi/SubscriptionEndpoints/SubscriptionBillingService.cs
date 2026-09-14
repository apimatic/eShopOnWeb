using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> ListMineAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan AmbiguousOutcomeCoolingPeriod = TimeSpan.FromSeconds(30);

    private readonly IMaxioSubscriptionGateway _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionLockManager _locks;

    public SubscriptionBillingService(
        IMaxioSubscriptionGateway maxio,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionLockManager locks)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _locks = locks;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken) =>
        (await _maxio.ListPlansAsync(cancellationToken)).Select(MapPlan).ToList();

    public async Task<SubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var normalizedHandle = productHandle.Trim();
        if (normalizedHandle.Length == 0)
        {
            throw new SubscriptionRequestException(HttpStatusCode.BadRequest, "A product handle is required.");
        }

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var selectedPlan = plans.SingleOrDefault(x => string.Equals(x.Handle, normalizedHandle, StringComparison.Ordinal));
        if (selectedPlan is null)
        {
            throw new SubscriptionRequestException(HttpStatusCode.NotFound, "The requested subscription plan is not available.");
        }

        if (selectedPlan.RequiresPaymentMethod)
        {
            throw new SubscriptionRequestException(
                HttpStatusCode.Conflict,
                "The requested plan currently requires a payment method and cannot be enrolled through this flow.");
        }

        var lockKey = $"{user.Id}\n{selectedPlan.Handle}";
        await using var heldLock = await _locks.AcquireAsync(lockKey, cancellationToken);

        var customerReference = StableReference("eshop-customer", user.Id);
        var subscriptionReference = StableReference("eshop-subscription", $"{user.Id}\n{selectedPlan.Handle}");
        var (record, recordWasCreated) = await GetOrCreateRecordAsync(
            user.Id,
            selectedPlan.Handle,
            customerReference,
            subscriptionReference,
            cancellationToken);

        var existing = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            EnsureProductMatches(existing, selectedPlan.Handle);
            await MarkCompletedAsync(record, existing.Id, cancellationToken);
            return MapSubscription(existing);
        }

        if (!recordWasCreated && record.Status == SubscriptionBillingStatus.Completed)
        {
            record.MarkReconciliationRequired();
            await SaveReconciliationStateAsync(cancellationToken);
            throw new SubscriptionRequestException(
                HttpStatusCode.ServiceUnavailable,
                "The recorded subscription is temporarily unavailable in Maxio and is being reconciled.");
        }

        if (!recordWasCreated && DateTimeOffset.UtcNow - record.UpdatedAt < AmbiguousOutcomeCoolingPeriod)
        {
            throw new SubscriptionRequestException(
                HttpStatusCode.ServiceUnavailable,
                "The previous enrollment outcome is still being reconciled. Retry shortly.");
        }

        record.MarkPending();
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SubscriptionRequestException(
                HttpStatusCode.ServiceUnavailable,
                "Another enrollment attempt is already in progress. Retry shortly.");
        }

        var email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionRequestException(HttpStatusCode.Conflict, "The authenticated account has no billing email.");
        }

        var (firstName, lastName) = GetCustomerName(user, email);
        await _maxio.EnsureCustomerAsync(customerReference, firstName, lastName, email, cancellationToken);

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customerReference,
                subscriptionReference,
                selectedPlan.Handle,
                cancellationToken);
            EnsureProductMatches(created, selectedPlan.Handle);
            await MarkCompletedAsync(record, created.Id, cancellationToken);
            return MapSubscription(created);
        }
        catch (MaxioBillingException ex) when (ex.OutcomeMayBeAmbiguous)
        {
            var reconciled = await _maxio.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                EnsureProductMatches(reconciled, selectedPlan.Handle);
                await MarkCompletedAsync(record, reconciled.Id, cancellationToken);
                return MapSubscription(reconciled);
            }

            record.MarkReconciliationRequired();
            await _catalogContext.SaveChangesAsync(cancellationToken);
            throw new SubscriptionRequestException(
                HttpStatusCode.ServiceUnavailable,
                "The enrollment outcome is not yet known. It is safe to retry shortly.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> ListMineAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customerReference = StableReference("eshop-customer", user.Id);
        return (await _maxio.ListCustomerSubscriptionsAsync(customerReference, cancellationToken))
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionRequestException(HttpStatusCode.Unauthorized, "The access token has no user identity.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new SubscriptionRequestException(HttpStatusCode.Unauthorized, "The access-token user no longer exists.");
    }

    private async Task<(SubscriptionBillingRecord Record, bool WasCreated)> GetOrCreateRecordAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _catalogContext.SubscriptionBillingRecords
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var created = new SubscriptionBillingRecord(userId, productHandle, customerReference, subscriptionReference);
        _catalogContext.SubscriptionBillingRecords.Add(created);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return (created, true);
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(created).State = EntityState.Detached;
            var racedRecord = await _catalogContext.SubscriptionBillingRecords
                .SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
            return (racedRecord, false);
        }
    }

    private async Task SaveReconciliationStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another instance already advanced the same enrollment record.
        }
    }

    private async Task MarkCompletedAsync(
        SubscriptionBillingRecord record,
        int maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        record.MarkCompleted(maxioSubscriptionId);
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureProductMatches(MaxioSubscription subscription, string expectedHandle)
    {
        if (!string.Equals(subscription.ProductHandle, expectedHandle, StringComparison.Ordinal))
        {
            throw new MaxioBillingException("Maxio returned a subscription for an unexpected product.");
        }
    }

    private static (string FirstName, string LastName) GetCustomerName(ApplicationUser user, string email)
    {
        var fallback = email.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        var firstName = string.IsNullOrWhiteSpace(user.FirstName) ? fallback : user.FirstName.Trim();
        var lastName = string.IsNullOrWhiteSpace(user.LastName) ? "eShop Customer" : user.LastName.Trim();
        return (Limit(firstName, 100, "eShop"), Limit(lastName, 100, "Customer"));
    }

    private static string Limit(string value, int maxLength, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value[..Math.Min(value.Length, maxLength)];

    private static string StableReference(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
    }

    private static SubscriptionPlanResponse MapPlan(MaxioPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit, plan.RequiresPaymentMethod);

    private static SubscriptionResponse MapSubscription(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Reference,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.Currency,
            subscription.State,
            subscription.NextBillingDate,
            subscription.Interval,
            subscription.IntervalUnit);
}

public sealed class SubscriptionLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
