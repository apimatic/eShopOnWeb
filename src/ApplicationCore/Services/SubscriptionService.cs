using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProvisioningLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan ProvisioningLease = TimeSpan.FromMinutes(2);
    private const string CustomerReferencePrefix = "eshop-user-";
    private const string SubscriptionReferencePrefix = "eshop-sub-";

    private readonly IMaxioBillingGateway _billingGateway;
    private readonly ISubscriptionRecordStore _recordStore;

    public SubscriptionService(IMaxioBillingGateway billingGateway, ISubscriptionRecordStore recordStore)
    {
        _billingGateway = billingGateway;
        _recordStore = recordStore;
    }

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _billingGateway.GetPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        string userId,
        string email,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(productHandle);

        var lockKey = string.Concat(userId, "\n", productHandle);
        var gate = ProvisioningLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            return await SubscribeCoreAsync(userId, email, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var customer = await _billingGateway.FindCustomerAsync(CustomerReference(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var expectedPrefix = SubscriptionReferencePrefix + userId + "-";
        var subscriptions = (await _billingGateway.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Where(subscription =>
                subscription.Reference.StartsWith(expectedPrefix, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(subscription.ProductHandle))
            .OrderBy(subscription => subscription.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var subscription in subscriptions)
        {
            await ReconcileRecordAsync(userId, customer.Reference, subscription, cancellationToken);
        }

        return subscriptions;
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        string userId,
        string email,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(userId);
        var subscriptionReference = SubscriptionReference(userId, productHandle);
        var record = await _recordStore.GetAsync(userId, productHandle, cancellationToken);

        if (record is not null)
        {
            var existing = await _billingGateway.FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
            if (existing is not null)
            {
                record.MarkProvisioned(existing.CustomerId, existing.Id);
                await _recordStore.SaveAsync(record, cancellationToken);
                return new SubscribeResult(existing, false);
            }

            if (!record.IsProvisioned && DateTimeOffset.UtcNow - record.LastAttemptAtUtc < ProvisioningLease)
            {
                throw new SubscriptionProvisioningException();
            }
        }
        else
        {
            var existing = await _billingGateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await ReconcileRecordAsync(userId, customerReference, existing, cancellationToken);
                return new SubscribeResult(existing, false);
            }
        }

        var plans = await _billingGateway.GetPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        if (record is not null)
        {
            record.MarkAttempt();
            await _recordStore.SaveAsync(record, cancellationToken);
        }
        else
        {
            record = new SubscriptionRecord(userId, productHandle, customerReference, subscriptionReference);
            if (!await _recordStore.TryAddAsync(record, cancellationToken))
            {
                throw new SubscriptionProvisioningException();
            }
        }

        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);
        var subscription = await _billingGateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
        var wasCreated = false;

        if (subscription is null)
        {
            try
            {
                subscription = await _billingGateway.CreateSubscriptionAsync(
                    customer.Id,
                    productHandle,
                    subscriptionReference,
                    cancellationToken);
                wasCreated = true;
            }
            catch (BillingProviderException exception) when (exception.StatusCode == 422)
            {
                subscription = await _billingGateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (subscription is null)
                {
                    record.MarkProvisioningFailed();
                    await _recordStore.SaveAsync(record, cancellationToken);
                    throw;
                }
            }
        }

        record.MarkProvisioned(customer.Id, subscription.Id);
        await _recordStore.SaveAsync(record, cancellationToken);
        return new SubscribeResult(subscription, wasCreated);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        string userId,
        string email,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(userId);
        var customer = await _billingGateway.FindCustomerAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        try
        {
            return await _billingGateway.CreateCustomerAsync(reference, email, firstName, "Customer", cancellationToken);
        }
        catch (BillingProviderException exception) when (exception.StatusCode == 422)
        {
            var existing = await _billingGateway.FindCustomerAsync(reference, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    private async Task ReconcileRecordAsync(
        string userId,
        string customerReference,
        BillingSubscription subscription,
        CancellationToken cancellationToken)
    {
        var record = await _recordStore.GetAsync(userId, subscription.ProductHandle, cancellationToken);
        if (record is null)
        {
            record = new SubscriptionRecord(userId, subscription.ProductHandle, customerReference, subscription.Reference);
            if (!await _recordStore.TryAddAsync(record, cancellationToken))
            {
                record = await _recordStore.GetAsync(userId, subscription.ProductHandle, cancellationToken);
            }
        }

        if (record is not null)
        {
            record.MarkProvisioned(subscription.CustomerId, subscription.Id);
            await _recordStore.SaveAsync(record, cancellationToken);
        }
    }

    private static string CustomerReference(string userId) => CustomerReferencePrefix + userId;

    private static string SubscriptionReference(string userId, string productHandle) =>
        SubscriptionReferencePrefix + userId + "-" + productHandle;
}
