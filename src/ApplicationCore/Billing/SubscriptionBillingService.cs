using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum SubscribeOutcome
{
    Created,
    Existing,
    Pending
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, BillingSubscription? Subscription);

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.") { }
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken);
}

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly IMaxioBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _enrollmentStore;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        ISubscriptionEnrollmentStore enrollmentStore)
    {
        _gateway = gateway;
        _enrollmentStore = enrollmentStore;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var customer = await _gateway.FindCustomerAsync(CustomerReference(shopper.Subject), cancellationToken);
        return customer is null
            ? Array.Empty<BillingSubscription>()
            : await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.Trim();
        var plan = await _gateway.FindPlanAsync(normalizedHandle, cancellationToken);
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(normalizedHandle);
        }

        var customerReference = CustomerReference(shopper.Subject);
        var subscriptionReference = SubscriptionReference(shopper.Subject, normalizedHandle);
        var lockKey = $"{shopper.Subject}\n{normalizedHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));

        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var lease = await _enrollmentStore.AcquireAsync(
                shopper.Subject,
                normalizedHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);

            if (lease.Status == EnrollmentLeaseStatus.InProgress)
            {
                return new SubscribeResult(SubscribeOutcome.Pending, null);
            }

            if (lease.Status == EnrollmentLeaseStatus.Rejected)
            {
                throw new BillingProviderException(
                    BillingFailureKind.Rejected,
                    lease.LastSafeError ?? "Maxio rejected this subscription request.");
            }

            try
            {
                if (lease.Status == EnrollmentLeaseStatus.Confirmed)
                {
                    var reconciled = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                    if (reconciled is not null)
                    {
                        if (lease.MaxioCustomerId.HasValue)
                        {
                            await _enrollmentStore.ConfirmAsync(
                                lease.EnrollmentId,
                                lease.Owner,
                                lease.MaxioCustomerId.Value,
                                reconciled.Id,
                                reconciled.State,
                                cancellationToken);
                        }

                        return new SubscribeResult(SubscribeOutcome.Existing, reconciled);
                    }

                    await _enrollmentStore.MarkNeedsReconciliationAsync(
                        lease.EnrollmentId,
                        lease.Owner,
                        ReconciliationTarget.Subscription,
                        "The existing Maxio subscription could not be found and requires reconciliation.",
                        cancellationToken);
                    return new SubscribeResult(SubscribeOutcome.Pending, null);
                }

                BillingCustomer? customer = null;
                if (lease.Status == EnrollmentLeaseStatus.ReconcileOnly)
                {
                    if (lease.ReconciliationTarget == ReconciliationTarget.Subscription)
                    {
                        var reconciled = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                        if (reconciled is not null)
                        {
                            if (lease.MaxioCustomerId.HasValue)
                            {
                                await _enrollmentStore.ConfirmAsync(
                                    lease.EnrollmentId,
                                    lease.Owner,
                                    lease.MaxioCustomerId.Value,
                                    reconciled.Id,
                                    reconciled.State,
                                    cancellationToken);
                            }

                            return new SubscribeResult(SubscribeOutcome.Existing, reconciled);
                        }

                        await _enrollmentStore.MarkNeedsReconciliationAsync(
                            lease.EnrollmentId,
                            lease.Owner,
                            lease.ReconciliationTarget,
                            lease.LastSafeError ?? "The prior Maxio operation is still being reconciled.",
                            cancellationToken);
                        return new SubscribeResult(SubscribeOutcome.Pending, null);
                    }

                    customer = await _gateway.FindCustomerAsync(customerReference, cancellationToken);
                    if (customer is null)
                    {
                        await _enrollmentStore.MarkNeedsReconciliationAsync(
                            lease.EnrollmentId,
                            lease.Owner,
                            ReconciliationTarget.Customer,
                            lease.LastSafeError ?? "The prior Maxio customer operation is still being reconciled.",
                            cancellationToken);
                        return new SubscribeResult(SubscribeOutcome.Pending, null);
                    }

                    await _enrollmentStore.RecordCustomerAsync(
                        lease.EnrollmentId,
                        lease.Owner,
                        customer.Id,
                        cancellationToken);
                }

                customer ??= await _gateway.FindCustomerAsync(customerReference, cancellationToken);
                if (customer is null)
                {
                    try
                    {
                        customer = await _gateway.CreateCustomerAsync(
                            customerReference,
                            shopper.FirstName,
                            shopper.LastName,
                            shopper.Email,
                            cancellationToken);
                    }
                    catch (BillingProviderException ex) when (ex.IsAmbiguousWrite)
                    {
                        await _enrollmentStore.MarkNeedsReconciliationAsync(
                            lease.EnrollmentId,
                            lease.Owner,
                            ReconciliationTarget.Customer,
                            ex.Message,
                            cancellationToken);
                        return new SubscribeResult(SubscribeOutcome.Pending, null);
                    }
                }

                await _enrollmentStore.RecordCustomerAsync(
                    lease.EnrollmentId,
                    lease.Owner,
                    customer.Id,
                    cancellationToken);

                var existing = await _gateway.FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    await _enrollmentStore.ConfirmAsync(
                        lease.EnrollmentId,
                        lease.Owner,
                        customer.Id,
                        existing.Id,
                        existing.State,
                        cancellationToken);
                    return new SubscribeResult(SubscribeOutcome.Existing, existing);
                }

                BillingSubscription created;
                try
                {
                    created = await _gateway.CreateSubscriptionAsync(
                        normalizedHandle,
                        customerReference,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (BillingProviderException ex) when (ex.IsAmbiguousWrite)
                {
                    await _enrollmentStore.MarkNeedsReconciliationAsync(
                        lease.EnrollmentId,
                        lease.Owner,
                        ReconciliationTarget.Subscription,
                        ex.Message,
                        cancellationToken);
                    return new SubscribeResult(SubscribeOutcome.Pending, null);
                }

                await _enrollmentStore.ConfirmAsync(
                    lease.EnrollmentId,
                    lease.Owner,
                    customer.Id,
                    created.Id,
                    created.State,
                    cancellationToken);
                return new SubscribeResult(SubscribeOutcome.Created, created);
            }
            catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.Rejected)
            {
                await _enrollmentStore.MarkRejectedAsync(
                    lease.EnrollmentId,
                    lease.Owner,
                    ex.Message,
                    cancellationToken);
                throw;
            }
            catch
            {
                await _enrollmentStore.ReleaseAsync(lease.EnrollmentId, lease.Owner, cancellationToken);
                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    private static string CustomerReference(string subject) => $"eshop-c-{Hash(subject)}";

    private static string SubscriptionReference(string subject, string productHandle) =>
        $"eshop-s-{Hash($"{subject}\n{productHandle}")}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
