using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-user:";
    private const string SubscriptionReferencePrefix = "eshop-sub:";
    private static readonly object LockSync = new();
    private static readonly Dictionary<string, LockEntry> Locks = new(StringComparer.Ordinal);
    private readonly CatalogContext _dbContext;
    private readonly ISubscriptionBillingGateway _gateway;

    public SubscriptionService(CatalogContext dbContext, ISubscriptionBillingGateway gateway)
    {
        _dbContext = dbContext;
        _gateway = gateway;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return _gateway.ListPlansAsync(cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length == 0 || productHandle.Length > 255)
        {
            throw new BillingRequestException("A valid productHandle is required.", 400);
        }

        using var enrollmentLock = await AcquireLockAsync(
            $"{user.Id}\n{productHandle}",
            cancellationToken);

        var plan = await _gateway.FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingRequestException("The requested subscription plan was not found.", 404);
        }

        var customerReference = CustomerReference(user.Id);
        var subscriptionReference = SubscriptionReference(user.Id, productHandle);
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            candidate => candidate.UserId == user.Id && candidate.ProductHandle == productHandle,
            cancellationToken);

        var existingSubscription = await _gateway.FindSubscriptionAsync(
            subscriptionReference,
            cancellationToken);
        if (existingSubscription is not null)
        {
            await ConfirmEnrollmentAsync(enrollment, user.Id, productHandle, subscriptionReference, existingSubscription, cancellationToken);
            return existingSubscription;
        }

        if (enrollment is not null
            && enrollment.Status is SubscriptionEnrollment.Pending or SubscriptionEnrollment.Indeterminate)
        {
            throw new BillingRequestException(
                "A previous subscription request still has an unknown outcome; no second enrollment was sent.",
                409);
        }

        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        await SaveCustomerLinkAsync(user.Id, customer, cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(user.Id, productHandle, subscriptionReference);
            _dbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(
                    candidate => candidate.UserId == user.Id && candidate.ProductHandle == productHandle,
                    cancellationToken);
                throw new BillingRequestException(
                    "A subscription request for this plan is already in progress.",
                    409);
            }
        }
        else
        {
            enrollment.Retry();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var created = await _gateway.CreateSubscriptionAsync(
                productHandle,
                customer.MaxioCustomerId,
                subscriptionReference,
                cancellationToken);
            enrollment.Confirm(created.MaxioSubscriptionId);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (BillingProviderException exception) when (exception.OutcomeMayBeUnknown)
        {
            var reconciled = await ReconcileSubscriptionAsync(
                customer.MaxioCustomerId,
                subscriptionReference,
                cancellationToken);
            if (reconciled is not null)
            {
                enrollment.Confirm(reconciled.MaxioSubscriptionId);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return reconciled;
            }

            enrollment.MarkIndeterminate();
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new BillingProviderException(
                "The subscription outcome could not be confirmed; no second enrollment will be sent.",
                outcomeMayBeUnknown: true,
                innerException: exception);
        }
        catch (BillingProviderException)
        {
            enrollment.MarkRejected();
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _gateway.FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(
            customer.MaxioCustomerId,
            cancellationToken);
        var referencePrefix = $"{SubscriptionReferencePrefix}{user.Id}:";
        var owned = subscriptions
            .Where(subscription => subscription.Reference?.StartsWith(referencePrefix, StringComparison.Ordinal) == true)
            .ToList();

        foreach (var subscription in owned)
        {
            var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
                candidate => candidate.SubscriptionReference == subscription.Reference,
                cancellationToken);
            if (enrollment is not null)
            {
                enrollment.Confirm(subscription.MaxioSubscriptionId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return owned;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _gateway.CreateCustomerAsync(user, reference, cancellationToken);
        }
        catch (BillingProviderException exception)
            when (exception.ProviderStatusCode == 422 || exception.OutcomeMayBeUnknown)
        {
            var reconciled = await _gateway.FindCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    private async Task SaveCustomerLinkAsync(
        string userId,
        BillingCustomer customer,
        CancellationToken cancellationToken)
    {
        var link = await _dbContext.MaxioCustomerLinks.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId,
            cancellationToken);
        if (link is null)
        {
            link = new MaxioCustomerLink(userId, customer.MaxioCustomerId, customer.Reference);
            _dbContext.MaxioCustomerLinks.Add(link);
        }
        else
        {
            link.Refresh(customer.MaxioCustomerId);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(link).State = EntityState.Detached;
        }
    }

    private async Task<CustomerSubscription?> ReconcileSubscriptionAsync(
        int maxioCustomerId,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var found = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (found is not null)
        {
            return found;
        }

        var customerSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(
            maxioCustomerId,
            cancellationToken);
        return customerSubscriptions.SingleOrDefault(
            subscription => subscription.Reference == subscriptionReference);
    }

    private async Task ConfirmEnrollmentAsync(
        SubscriptionEnrollment? enrollment,
        string userId,
        string productHandle,
        string subscriptionReference,
        CustomerSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(userId, productHandle, subscriptionReference);
            _dbContext.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.Confirm(subscription.MaxioSubscriptionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static string SubscriptionReference(string userId, string productHandle) =>
        $"{SubscriptionReferencePrefix}{userId}:{productHandle}";

    private static async Task<IDisposable> AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        LockEntry entry;
        lock (LockSync)
        {
            if (!Locks.TryGetValue(key, out entry!))
            {
                entry = new LockEntry();
                Locks.Add(key, entry);
            }

            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            return new LockReleaser(key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseGate: false);
            throw;
        }
    }

    private static void ReleaseReference(string key, LockEntry entry, bool releaseGate)
    {
        if (releaseGate)
        {
            entry.Gate.Release();
        }

        lock (LockSync)
        {
            entry.Users--;
            if (entry.Users == 0)
            {
                Locks.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly string _key;
        private LockEntry? _entry;

        public LockReleaser(string key, LockEntry entry)
        {
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is not null)
            {
                ReleaseReference(_key, entry, releaseGate: true);
            }
        }
    }
}
