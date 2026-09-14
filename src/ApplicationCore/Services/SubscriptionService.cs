using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();
    private readonly IMaxioBillingGateway _billingGateway;
    private readonly IRepository<SubscriptionRecord> _subscriptionRepository;

    public SubscriptionService(
        IMaxioBillingGateway billingGateway,
        IRepository<SubscriptionRecord> subscriptionRepository)
    {
        _billingGateway = billingGateway;
        _subscriptionRepository = subscriptionRepository;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? string.Empty);
        }

        var normalizedHandle = productHandle.Trim();
        var subscriptionReference = CreateReference("eshop-sub", user.Id, normalizedHandle);
        var gate = SubscriptionLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await _billingGateway.ListPlansAsync(cancellationToken);
            var selectedPlan = plans.SingleOrDefault(plan =>
                string.Equals(plan.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
            if (selectedPlan is null)
            {
                throw new SubscriptionPlanNotFoundException(normalizedHandle);
            }

            var customerReference = CreateReference("eshop-user", user.Id);
            var customer = await _billingGateway.EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscription = await _billingGateway.EnsureSubscriptionAsync(
                selectedPlan.Handle,
                customer.Id,
                subscriptionReference,
                cancellationToken);

            await ReconcileLocalRecordAsync(
                user.Id,
                selectedPlan.Handle,
                customerReference,
                subscriptionReference,
                customer.Id,
                subscription.Id,
                cancellationToken);

            return subscription;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        BillingUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = CreateReference("eshop-user", user.Id);
        var customer = await _billingGateway.FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        return await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task ReconcileLocalRecordAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        long maxioCustomerId,
        long maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        var specification = new SubscriptionRecordByReferenceSpecification(subscriptionReference);
        var existing = await _subscriptionRepository.FirstOrDefaultAsync(specification, cancellationToken);
        if (existing is null)
        {
            try
            {
                await _subscriptionRepository.AddAsync(
                    new SubscriptionRecord(
                        userId,
                        productHandle,
                        customerReference,
                        subscriptionReference,
                        maxioCustomerId,
                        maxioSubscriptionId),
                    cancellationToken);
            }
            catch
            {
                // Another app instance may have persisted the same deterministic Maxio reference first.
                existing = await _subscriptionRepository.FirstOrDefaultAsync(specification, cancellationToken);
                if (existing is null)
                {
                    throw;
                }
            }
            return;
        }

        existing.Reconcile(maxioCustomerId, maxioSubscriptionId);
        await _subscriptionRepository.UpdateAsync(existing, cancellationToken);
    }

    internal static string CreateReference(string prefix, params string[] values)
    {
        var input = string.Join('|', values);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{prefix}-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
