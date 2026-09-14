using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IBillingGateway _billingGateway;
    private readonly ISubscriptionRecordStore _recordStore;
    private static readonly object OperationLocksSync = new();
    private static readonly Dictionary<string, OperationLock> OperationLocks = new();

    public SubscriptionService(IBillingGateway billingGateway, ISubscriptionRecordStore recordStore)
    {
        _billingGateway = billingGateway;
        _recordStore = recordStore;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);

        var plans = await _billingGateway.ListPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingProviderValidationException("The requested subscription plan is not available.");
        }

        var customerReference = CreateReference("customer", user.Id);
        var subscriptionReference = CreateReference("subscription", $"{user.Id}:{plan.Handle}");
        using var operationLock = await AcquireOperationLockAsync(subscriptionReference, cancellationToken);

        var existing = await _billingGateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            EnsureCreatedSuccessfully(existing);
            await _recordStore.SynchronizeAsync(user.Id, existing, cancellationToken);
            return new SubscribeResult(existing, false);
        }

        var customer = await _billingGateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _billingGateway.CreateCustomerAsync(user, customerReference, cancellationToken);
            }
            catch (BillingProviderValidationException)
            {
                // A concurrent request may have won the unique customer-reference race.
                customer = await _billingGateway.FindCustomerAsync(customerReference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        SubscriptionDetails subscription;
        try
        {
            subscription = await _billingGateway.CreateSubscriptionAsync(
                customer.Reference,
                plan.Handle,
                subscriptionReference,
                cancellationToken);
        }
        catch (BillingProviderValidationException)
        {
            // Subscription references are unique in Maxio. Resolve a concurrent/retried create.
            var concurrentlyCreated = await _billingGateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (concurrentlyCreated is null)
            {
                throw;
            }

            subscription = concurrentlyCreated;
        }

        EnsureCreatedSuccessfully(subscription);
        await _recordStore.SynchronizeAsync(user.Id, subscription, cancellationToken);
        return new SubscribeResult(subscription, true);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        BillingUser user,
        CancellationToken cancellationToken = default)
    {
        var customerReference = CreateReference("customer", user.Id);
        var customer = await _billingGateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            await _recordStore.SynchronizeAsync(user.Id, subscription, cancellationToken);
        }

        return subscriptions;
    }

    private static string CreateReference(string resource, string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"eshop-{resource}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void EnsureCreatedSuccessfully(SubscriptionDetails subscription)
    {
        if (string.Equals(subscription.State, "failed_to_create", StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderValidationException("Maxio could not create the subscription.");
        }
    }

    private static async Task<IDisposable> AcquireOperationLockAsync(string key, CancellationToken cancellationToken)
    {
        OperationLock operationLock;
        lock (OperationLocksSync)
        {
            if (!OperationLocks.TryGetValue(key, out operationLock!))
            {
                operationLock = new OperationLock();
                OperationLocks.Add(key, operationLock);
            }

            operationLock.ReferenceCount++;
        }

        try
        {
            await operationLock.Semaphore.WaitAsync(cancellationToken);
            return new OperationLockLease(key, operationLock);
        }
        catch
        {
            ReleaseOperationLockReference(key, operationLock);
            throw;
        }
    }

    private static void ReleaseOperationLockReference(string key, OperationLock operationLock)
    {
        lock (OperationLocksSync)
        {
            operationLock.ReferenceCount--;
            if (operationLock.ReferenceCount == 0)
            {
                OperationLocks.Remove(key);
                operationLock.Semaphore.Dispose();
            }
        }
    }

    private sealed class OperationLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class OperationLockLease : IDisposable
    {
        private readonly string _key;
        private OperationLock? _operationLock;

        public OperationLockLease(string key, OperationLock operationLock)
        {
            _key = key;
            _operationLock = operationLock;
        }

        public void Dispose()
        {
            var operationLock = Interlocked.Exchange(ref _operationLock, null);
            if (operationLock is null)
            {
                return;
            }

            operationLock.Semaphore.Release();
            ReleaseOperationLockReference(_key, operationLock);
        }
    }
}
