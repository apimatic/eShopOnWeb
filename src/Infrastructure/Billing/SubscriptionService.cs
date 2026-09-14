using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);

    private readonly CatalogContext _catalogContext;
    private readonly ISubscriptionBillingGateway _billingGateway;
    private readonly ISubscriptionOperationLock _operationLock;
    private readonly TimeProvider _timeProvider;

    public SubscriptionService(
        CatalogContext catalogContext,
        ISubscriptionBillingGateway billingGateway,
        ISubscriptionOperationLock operationLock,
        TimeProvider timeProvider)
    {
        _catalogContext = catalogContext;
        _billingGateway = billingGateway;
        _operationLock = operationLock;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionResult?> SubscribeAsync(
        BillingCustomerIdentity identity,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var lockKey = $"{identity.UserId}:{productHandle}";
        using var operationLease = await _operationLock.AcquireAsync(lockKey, cancellationToken);

        var plan = await _billingGateway.FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var customerReference = BuildReference("eshop-customer", identity.UserId);
        var subscriptionReference = BuildReference("eshop-subscription", $"{identity.UserId}:{plan.Handle}");
        var now = _timeProvider.GetUtcNow();
        var reservation = await FindOrReserveAsync(
            identity.UserId,
            plan.Handle,
            customerReference,
            subscriptionReference,
            now,
            cancellationToken);
        var record = reservation.Record;

        if (!reservation.Created &&
            record.Status == SubscriptionProvisioningStatus.Pending &&
            record.UpdatedAt < now - PendingLease)
        {
            record.BeginAttempt(now);
            await SaveAttemptAsync(cancellationToken);
        }
        else if (!reservation.Created &&
                 record.Status == SubscriptionProvisioningStatus.Pending &&
                 record.MaxioSubscriptionId is null)
        {
            var pendingSubscription = await _billingGateway.FindSubscriptionAsync(
                subscriptionReference, cancellationToken);
            if (pendingSubscription is not null)
            {
                ValidateRemoteSubscription(pendingSubscription, plan.Handle, record.MaxioCustomerId);
                record.Complete(pendingSubscription.CustomerId, pendingSubscription.Id, now);
                await _catalogContext.SaveChangesAsync(cancellationToken);
                return new SubscriptionResult(pendingSubscription, false);
            }

            if (record.UpdatedAt >= now - PendingLease)
            {
                throw new SubscriptionOperationInProgressException();
            }
        }
        else if (!reservation.Created && record.Status == SubscriptionProvisioningStatus.Succeeded)
        {
            var existingSubscription = await _billingGateway.FindSubscriptionAsync(
                subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                ValidateRemoteSubscription(existingSubscription, plan.Handle, record.MaxioCustomerId);
                return new SubscriptionResult(existingSubscription, false);
            }

            record.BeginAttempt(now);
            await SaveAttemptAsync(cancellationToken);
        }
        else if (!reservation.Created && record.Status == SubscriptionProvisioningStatus.Failed)
        {
            record.BeginAttempt(now);
            await SaveAttemptAsync(cancellationToken);
        }

        try
        {
            var customer = await EnsureCustomerAsync(identity, customerReference, cancellationToken);
            var existingSubscription = await _billingGateway.FindSubscriptionAsync(
                subscriptionReference, cancellationToken);

            if (existingSubscription is not null)
            {
                ValidateRemoteSubscription(existingSubscription, plan.Handle, customer.Id);
                record.Complete(customer.Id, existingSubscription.Id, _timeProvider.GetUtcNow());
                await _catalogContext.SaveChangesAsync(cancellationToken);
                return new SubscriptionResult(existingSubscription, false);
            }

            var subscription = await CreateOrRecoverSubscriptionAsync(
                customer.Id,
                plan.Handle,
                subscriptionReference,
                cancellationToken);
            ValidateRemoteSubscription(subscription, plan.Handle, customer.Id);

            record.Complete(customer.Id, subscription.Id, _timeProvider.GetUtcNow());
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return new SubscriptionResult(subscription, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            record.Fail(exception.GetType().Name, _timeProvider.GetUtcNow());
            await _catalogContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        BillingCustomerIdentity identity,
        CancellationToken cancellationToken)
    {
        var reference = BuildReference("eshop-customer", identity.UserId);
        var customer = await _billingGateway.FindCustomerAsync(reference, cancellationToken);
        return customer is null
            ? Array.Empty<BillingSubscription>()
            : await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<(SubscriptionProvisioningRecord Record, bool Created)> FindOrReserveAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await _catalogContext.SubscriptionProvisioningRecords.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle,
            cancellationToken);
        if (record is not null)
        {
            return (record, false);
        }

        record = new SubscriptionProvisioningRecord(
            userId,
            productHandle,
            customerReference,
            subscriptionReference,
            now);
        _catalogContext.SubscriptionProvisioningRecords.Add(record);

        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return (record, true);
        }
        catch (DbUpdateException)
        {
            _catalogContext.ChangeTracker.Clear();
            var existingRecord = await _catalogContext.SubscriptionProvisioningRecords.SingleOrDefaultAsync(
                item => item.UserId == userId && item.ProductHandle == productHandle,
                cancellationToken);
            if (existingRecord is null)
            {
                throw;
            }

            return (existingRecord, false);
        }
    }

    private async Task SaveAttemptAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SubscriptionOperationInProgressException(exception);
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerIdentity identity,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await _billingGateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _billingGateway.CreateCustomerAsync(identity, customerReference, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception, cancellationToken))
        {
            var recovered = await TryRecoverCustomerAsync(customerReference, cancellationToken);
            return recovered ?? throw new BillingProviderException(
                "Maxio customer creation could not be confirmed.", exception);
        }
    }

    private async Task<BillingSubscription> CreateOrRecoverSubscriptionAsync(
        long customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingGateway.CreateSubscriptionAsync(
                customerId, productHandle, subscriptionReference, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception, cancellationToken))
        {
            var recovered = await TryRecoverSubscriptionAsync(subscriptionReference, cancellationToken);
            return recovered ?? throw new BillingProviderException(
                "Maxio subscription creation could not be confirmed.", exception);
        }
    }

    private async Task<BillingCustomer?> TryRecoverCustomerAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingGateway.FindCustomerAsync(reference, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private async Task<BillingSubscription?> TryRecoverSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingGateway.FindSubscriptionAsync(reference, cancellationToken);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private static bool IsRecoverableProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is BillingProviderException or HttpRequestException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static void ValidateRemoteSubscription(
        BillingSubscription subscription,
        string productHandle,
        long? customerId)
    {
        if (!string.Equals(subscription.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase) ||
            customerId.HasValue && subscription.CustomerId != customerId.Value)
        {
            throw new BillingProviderException(
                "Maxio returned a subscription that does not match the requested customer and plan.");
        }
    }

    private static string BuildReference(string prefix, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
