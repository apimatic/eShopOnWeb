using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ProvisioningLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ContentionWait = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan[] ReconciliationDelays =
    {
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    };

    private readonly IMaxioBillingGateway _gateway;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _identityContext = identityContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(
        CancellationToken cancellationToken)
    {
        var plans = await _gateway.ListPlansAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionApiException(
                StatusCodes.Status400BadRequest,
                "A product handle is required.",
                "invalid_product_handle");
        }

        var user = await ResolveUserAsync(principal);
        ValidateCustomerProfile(user);

        await using var processLock = await KeyedProcessLock.AcquireAsync(user.Id, cancellationToken);

        var plans = await _gateway.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionApiException(
                StatusCodes.Status400BadRequest,
                "The selected subscription plan is unavailable.",
                "invalid_product_handle");
        }

        var customerReference = CreateReference("customer-v1", user.Id);
        var subscriptionReference = CreateReference("subscription-v1", user.Id, plan.Handle);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);

        var enrollment = await GetOrCreateEnrollmentAsync(
            user.Id,
            plan.Handle,
            subscriptionReference,
            cancellationToken);

        var existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteEnrollmentAsync(enrollment, existing.Id, cancellationToken);
            return new SubscribeResult(MapSubscription(existing), Created: false);
        }

        if (enrollment.State == BillingProvisioningState.Ambiguous)
        {
            throw AmbiguousOutcome();
        }

        var ownerId = Guid.NewGuid();
        if (!await AcquireEnrollmentLeaseAsync(enrollment, ownerId, cancellationToken))
        {
            var completed = await WaitForSubscriptionAsync(
                enrollment,
                subscriptionReference,
                cancellationToken);
            return new SubscribeResult(MapSubscription(completed), Created: false);
        }

        existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteEnrollmentAsync(enrollment, existing.Id, cancellationToken);
            return new SubscribeResult(MapSubscription(existing), Created: false);
        }

        try
        {
            var creation = await _gateway.CreateSubscriptionAsync(
                plan.Handle,
                customer.Reference,
                subscriptionReference,
                cancellationToken);
            await CompleteEnrollmentAsync(enrollment, creation.Subscription.Id, cancellationToken);
            return new SubscribeResult(MapSubscription(creation.Subscription), creation.Created);
        }
        catch (MaxioIntegrationException exception)
            when (exception.Kind == MaxioFailureKind.AmbiguousWrite)
        {
            var reconciled = await ReconcileSubscriptionAsync(
                subscriptionReference,
                cancellationToken);
            if (reconciled is not null)
            {
                await CompleteEnrollmentAsync(enrollment, reconciled.Id, cancellationToken);
                return new SubscribeResult(MapSubscription(reconciled), Created: false);
            }

            await MarkEnrollmentAmbiguousAsync(enrollment, cancellationToken);
            throw;
        }
        catch
        {
            await ReleaseEnrollmentLeaseAsync(enrollment, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customerReference = CreateReference("customer-v1", user.Id);
        var customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(
            customer.Id,
            cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        ApplicationUser user,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var link = await GetOrCreateCustomerLinkAsync(user.Id, customerReference, cancellationToken);
        var existing = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteCustomerLinkAsync(link, existing.Id, cancellationToken);
            return existing;
        }

        if (link.State == BillingProvisioningState.Ambiguous)
        {
            throw AmbiguousOutcome();
        }

        var ownerId = Guid.NewGuid();
        if (!await AcquireCustomerLeaseAsync(link, ownerId, cancellationToken))
        {
            return await WaitForCustomerAsync(link, customerReference, cancellationToken);
        }

        existing = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteCustomerLinkAsync(link, existing.Id, cancellationToken);
            return existing;
        }

        var profile = new MaxioCustomerProfile(user.FirstName!, user.LastName!, user.Email!);
        try
        {
            var customer = await _gateway.CreateCustomerAsync(
                profile,
                customerReference,
                cancellationToken);
            await CompleteCustomerLinkAsync(link, customer.Id, cancellationToken);
            return customer;
        }
        catch (MaxioIntegrationException exception)
            when (exception.Kind == MaxioFailureKind.AmbiguousWrite)
        {
            var reconciled = await ReconcileCustomerAsync(customerReference, cancellationToken);
            if (reconciled is not null)
            {
                await CompleteCustomerLinkAsync(link, reconciled.Id, cancellationToken);
                return reconciled;
            }

            await MarkCustomerAmbiguousAsync(link, cancellationToken);
            throw;
        }
        catch
        {
            await ReleaseCustomerLeaseAsync(link, cancellationToken);
            throw;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionApiException(
                StatusCodes.Status401Unauthorized,
                "Authentication is required.",
                "authentication_required");
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new SubscriptionApiException(
            StatusCodes.Status401Unauthorized,
            "The authenticated user no longer exists.",
            "user_not_found");
    }

    private static void ValidateCustomerProfile(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName) ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionApiException(
                StatusCodes.Status422UnprocessableEntity,
                "A first name, last name, and email are required before subscribing.",
                "billing_profile_incomplete");
        }
    }

    private async Task<MaxioCustomerLink> GetOrCreateCustomerLinkAsync(
        string userId,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _identityContext.MaxioCustomerLinks
            .SingleOrDefaultAsync(link => link.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.CustomerReference, reference, StringComparison.Ordinal))
            {
                throw new SubscriptionApiException(
                    StatusCodes.Status409Conflict,
                    "The local billing identity is inconsistent.",
                    "billing_identity_conflict");
            }

            return existing;
        }

        var link = new MaxioCustomerLink
        {
            UserId = userId,
            CustomerReference = reference,
            State = BillingProvisioningState.Pending
        };
        _identityContext.MaxioCustomerLinks.Add(link);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return link;
        }
        catch (DbUpdateException)
        {
            _identityContext.Entry(link).State = EntityState.Detached;
            return await _identityContext.MaxioCustomerLinks
                .SingleAsync(candidate => candidate.UserId == userId, cancellationToken);
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _identityContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            enrollment => enrollment.UserId == userId && enrollment.ProductHandle == productHandle,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            SubscriptionReference = reference,
            State = BillingProvisioningState.Pending
        };
        _identityContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _identityContext.Entry(enrollment).State = EntityState.Detached;
            return await _identityContext.SubscriptionEnrollments.SingleAsync(
                candidate => candidate.UserId == userId && candidate.ProductHandle == productHandle,
                cancellationToken);
        }
    }

    private Task<bool> AcquireCustomerLeaseAsync(
        MaxioCustomerLink link,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        AcquireLeaseAsync(
            () => link.LeaseId,
            () => link.LeaseExpiresAt,
            () => link.State,
            (lease, expires, state, version, updated) =>
            {
                link.LeaseId = lease;
                link.LeaseExpiresAt = expires;
                link.State = state;
                link.Version = version;
                link.UpdatedAt = updated;
            },
            () => _identityContext.Entry(link).ReloadAsync(cancellationToken),
            ownerId,
            abandonExpiredCreatingLease: false,
            cancellationToken);

    private Task<bool> AcquireEnrollmentLeaseAsync(
        SubscriptionEnrollment enrollment,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        AcquireLeaseAsync(
            () => enrollment.LeaseId,
            () => enrollment.LeaseExpiresAt,
            () => enrollment.State,
            (lease, expires, state, version, updated) =>
            {
                enrollment.LeaseId = lease;
                enrollment.LeaseExpiresAt = expires;
                enrollment.State = state;
                enrollment.Version = version;
                enrollment.UpdatedAt = updated;
            },
            () => _identityContext.Entry(enrollment).ReloadAsync(cancellationToken),
            ownerId,
            abandonExpiredCreatingLease: true,
            cancellationToken);

    private async Task<bool> AcquireLeaseAsync(
        Func<Guid?> getLease,
        Func<DateTimeOffset?> getLeaseExpiry,
        Func<BillingProvisioningState> getState,
        Action<Guid?, DateTimeOffset?, BillingProvisioningState, Guid, DateTimeOffset> update,
        Func<Task> reload,
        Guid ownerId,
        bool abandonExpiredCreatingLease,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ContentionWait)
        {
            var now = DateTimeOffset.UtcNow;
            if (getLease() is null || getLeaseExpiry() <= now)
            {
                if (abandonExpiredCreatingLease && getState() == BillingProvisioningState.Creating)
                {
                    update(null, null, BillingProvisioningState.Ambiguous, Guid.NewGuid(), now);
                    try
                    {
                        await _identityContext.SaveChangesAsync(cancellationToken);
                        return false;
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        await reload();
                        continue;
                    }
                }

                update(ownerId, now.Add(ProvisioningLease), BillingProvisioningState.Creating, Guid.NewGuid(), now);
                try
                {
                    await _identityContext.SaveChangesAsync(cancellationToken);
                    return true;
                }
                catch (DbUpdateConcurrencyException)
                {
                    await reload();
                    continue;
                }
            }

            await Task.Delay(250, cancellationToken);
            await reload();
        }

        return false;
    }

    private async Task<MaxioCustomer> WaitForCustomerAsync(
        MaxioCustomerLink link,
        string reference,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ContentionWait)
        {
            var customer = await _gateway.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null)
            {
                await CompleteCustomerLinkAsync(link, customer.Id, cancellationToken);
                return customer;
            }

            await Task.Delay(250, cancellationToken);
            await _identityContext.Entry(link).ReloadAsync(cancellationToken);
            if (link.State == BillingProvisioningState.Ambiguous)
            {
                break;
            }
        }

        throw AmbiguousOutcome();
    }

    private async Task<MaxioSubscription> WaitForSubscriptionAsync(
        SubscriptionEnrollment enrollment,
        string reference,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ContentionWait)
        {
            var subscription = await _gateway.FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                await CompleteEnrollmentAsync(enrollment, subscription.Id, cancellationToken);
                return subscription;
            }

            await Task.Delay(250, cancellationToken);
            await _identityContext.Entry(enrollment).ReloadAsync(cancellationToken);
            if (enrollment.State == BillingProvisioningState.Ambiguous)
            {
                break;
            }
        }

        throw AmbiguousOutcome();
    }

    private async Task<MaxioCustomer?> ReconcileCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        foreach (var delay in ReconciliationDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var customer = await _gateway.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }
        }

        return null;
    }

    private async Task<MaxioSubscription?> ReconcileSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        foreach (var delay in ReconciliationDelays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var subscription = await _gateway.FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        return null;
    }

    private async Task CompleteCustomerLinkAsync(
        MaxioCustomerLink link,
        int customerId,
        CancellationToken cancellationToken)
    {
        link.MaxioCustomerId = customerId;
        link.State = BillingProvisioningState.Succeeded;
        link.LeaseId = null;
        link.LeaseExpiresAt = null;
        link.Version = Guid.NewGuid();
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(link, cancellationToken);
    }

    private async Task CompleteEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = subscriptionId;
        enrollment.State = BillingProvisioningState.Succeeded;
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.Version = Guid.NewGuid();
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(enrollment, cancellationToken);
    }

    private async Task MarkCustomerAmbiguousAsync(
        MaxioCustomerLink link,
        CancellationToken cancellationToken)
    {
        link.State = BillingProvisioningState.Ambiguous;
        link.LeaseId = null;
        link.LeaseExpiresAt = null;
        link.Version = Guid.NewGuid();
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(link, cancellationToken);
    }

    private async Task MarkEnrollmentAmbiguousAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        enrollment.State = BillingProvisioningState.Ambiguous;
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.Version = Guid.NewGuid();
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(enrollment, cancellationToken);
    }

    private async Task ReleaseCustomerLeaseAsync(
        MaxioCustomerLink link,
        CancellationToken cancellationToken)
    {
        link.State = BillingProvisioningState.Pending;
        link.LeaseId = null;
        link.LeaseExpiresAt = null;
        link.Version = Guid.NewGuid();
        link.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(link, cancellationToken);
    }

    private async Task ReleaseEnrollmentLeaseAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        enrollment.State = BillingProvisioningState.Pending;
        enrollment.LeaseId = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.Version = Guid.NewGuid();
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveIgnoringResolvedConcurrencyAsync(enrollment, cancellationToken);
    }

    private async Task SaveIgnoringResolvedConcurrencyAsync(
        object entity,
        CancellationToken cancellationToken)
    {
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _identityContext.Entry(entity).ReloadAsync(cancellationToken);
        }
    }

    private static SubscriptionPlanDto MapPlan(MaxioPlan plan) => new(
        plan.Handle,
        plan.Name,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit);

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.Reference,
        subscription.ProductHandle,
        subscription.ProductName,
        subscription.ProductPriceInCents,
        subscription.CurrentBillingAmountInCents,
        subscription.State,
        subscription.NextBillingDate);

    private static string CreateReference(string purpose, params string[] values)
    {
        var material = string.Join("\u001f", values.Select(value => $"{value.Length}:{value}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"eshop-{purpose}-{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static SubscriptionApiException AmbiguousOutcome() => new(
        StatusCodes.Status503ServiceUnavailable,
        "The subscription outcome is still being reconciled; retry this request later.",
        "billing_outcome_ambiguous");

    private static class KeyedProcessLock
    {
        private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

        internal static async ValueTask<IAsyncDisposable> AcquireAsync(
            string key,
            CancellationToken cancellationToken)
        {
            Entry entry;
            while (true)
            {
                entry = Entries.GetOrAdd(key, static _ => new Entry());
                Interlocked.Increment(ref entry.ReferenceCount);
                if (Entries.TryGetValue(key, out var current) && ReferenceEquals(entry, current))
                {
                    break;
                }

                Interlocked.Decrement(ref entry.ReferenceCount);
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Releaser(key, entry);
            }
            catch
            {
                ReleaseReference(key, entry, releaseSemaphore: false);
                throw;
            }
        }

        private static void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
        {
            if (releaseSemaphore)
            {
                entry.Semaphore.Release();
            }

            if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
            {
                Entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }

        private sealed class Entry
        {
            internal readonly SemaphoreSlim Semaphore = new(1, 1);
            internal int ReferenceCount;
        }

        private sealed class Releaser : IAsyncDisposable
        {
            private readonly string _key;
            private Entry? _entry;

            internal Releaser(string key, Entry entry)
            {
                _key = key;
                _entry = entry;
            }

            public ValueTask DisposeAsync()
            {
                var entry = Interlocked.Exchange(ref _entry, null);
                if (entry is not null)
                {
                    ReleaseReference(_key, entry, releaseSemaphore: true);
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
