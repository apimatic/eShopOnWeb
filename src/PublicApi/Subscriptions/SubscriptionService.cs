using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, UserLockEntry> UserLocks = new();
    private static readonly TimeSpan PendingCreationSafetyWindow = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
        _maxio.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("productHandle is required.");
        }

        var user = await GetUserAsync(principal);
        var normalizedHandle = productHandle.Trim();
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionRequestException("The requested subscription plan is not available.");
        }

        using var userLock = await AcquireUserLockAsync(user.Id, cancellationToken);
        return await SubscribeUnderLockAsync(user, plan.Handle, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _maxio.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        return await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscriptionDto> SubscribeUnderLockAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);
        var ownsReservation = false;
        var record = await _catalogContext.SubscriptionRecords
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == productHandle, cancellationToken);

        if (record is null)
        {
            record = new SubscriptionRecord(user.Id, productHandle, subscriptionReference);
            _catalogContext.SubscriptionRecords.Add(record);

            try
            {
                await _catalogContext.SaveChangesAsync(cancellationToken);
                ownsReservation = true;
            }
            catch (DbUpdateException)
            {
                _catalogContext.Entry(record).State = EntityState.Detached;
                record = await _catalogContext.SubscriptionRecords
                    .SingleAsync(x => x.UserId == user.Id && x.ProductHandle == productHandle, cancellationToken);
            }
        }

        var existing = await _maxio.FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteRecordAsync(record, existing.Id, cancellationToken);
            return existing;
        }

        if (record.MaxioSubscriptionId.HasValue)
        {
            throw new MaxioApiException("The subscription recorded locally no longer exists in Maxio.", 502);
        }

        if (ownsReservation)
        {
            // This request durably reserved the user/plan pair before calling Maxio.
        }
        else if (record.CreationStartedAt < DateTimeOffset.UtcNow - PendingCreationSafetyWindow)
        {
            // A timed-out POST has an unknown outcome. The deterministic lookup above is the
            // recovery check; the safety window avoids racing Maxio's write visibility.
        }
        else
        {
            throw new SubscriptionInProgressException("Subscription creation is already in progress. Retry shortly.");
        }

        MaxioCustomer customer;
        SubscriptionDto subscription;
        try
        {
            customer = await EnsureCustomerAsync(user, cancellationToken);
            subscription = await _maxio.CreateSubscriptionAsync(
                productHandle,
                customer.Reference,
                record.SubscriptionReference,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            // A provider validation response is a known non-create outcome. Release this
            // reservation so a corrected request can retry immediately. Transport failures
            // retain it because the POST outcome may be unknown.
            _catalogContext.SubscriptionRecords.Remove(record);
            await _catalogContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        record.Complete(customer.Id, subscription.Id);
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _maxio.FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName
            ?? throw new SubscriptionRequestException("The authenticated user has no email address.");
        var displayName = email.Split('@', 2)[0];

        try
        {
            return await _maxio.CreateCustomerAsync(
                new MaxioCustomerInput(displayName, "Customer", email, reference),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Customer references are unique in Maxio. Another plan request may have won
            // the create race, so resolve the canonical customer before surfacing the error.
            var concurrentlyCreated = await _maxio.FindCustomerAsync(reference, cancellationToken);
            if (concurrentlyCreated is not null)
            {
                return concurrentlyCreated;
            }

            throw;
        }
    }

    private async Task CompleteRecordAsync(
        SubscriptionRecord record,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        if (record.MaxioSubscriptionId == subscriptionId)
        {
            return;
        }

        var customer = await _maxio.FindCustomerAsync(CustomerReference(record.UserId), cancellationToken)
            ?? throw new MaxioApiException("The Maxio customer for this subscription could not be found.", 502);
        record.Complete(customer.Id, subscriptionId);
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionRequestException("The bearer token does not contain a username.");
        }

        return await _userManager.FindByNameAsync(username)
            ?? throw new SubscriptionRequestException("The authenticated user no longer exists.");
    }

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";
    private static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";

    private static async Task<IDisposable> AcquireUserLockAsync(string userId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = UserLocks.GetOrAdd(userId, _ => new UserLockEntry());
            lock (entry)
            {
                if (!UserLocks.TryGetValue(userId, out var current) || !ReferenceEquals(entry, current))
                {
                    continue;
                }

                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new UserLockLease(userId, entry);
            }
            catch
            {
                ReleaseUserLock(userId, entry, releaseSemaphore: false);
                throw;
            }
        }
    }

    private static void ReleaseUserLock(string userId, UserLockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (entry)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                UserLocks.TryGetValue(userId, out var current) &&
                ReferenceEquals(entry, current))
            {
                UserLocks.TryRemove(userId, out _);
            }
        }
    }

    private sealed class UserLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class UserLockLease : IDisposable
    {
        private readonly string _userId;
        private UserLockEntry? _entry;

        public UserLockLease(string userId, UserLockEntry entry)
        {
            _userId = userId;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is not null)
            {
                ReleaseUserLock(_userId, entry, releaseSemaphore: true);
            }
        }
    }
}
