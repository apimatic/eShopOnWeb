using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListProductsForProductFamilyAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required to subscribe.", nameof(productHandle));
        }

        productHandle = productHandle.Trim();
        var handleLock = Locks.GetOrAdd($"{shopper.Reference}:{productHandle}", _ => new SemaphoreSlim(1, 1));
        await handleLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle, cancellationToken);
        }
        finally
        {
            handleLock.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.ReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var plan = await FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);
        var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for {Reference} on {Handle}.",
                existing.Id, shopper.Reference, productHandle);
            return new SubscribeResult(existing, Created: false);
        }

        var subscriptionReference = BuildSubscriptionReference(shopper.Reference, productHandle);
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null && byReference.IsLive)
        {
            return new SubscribeResult(byReference, Created: false);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id,
                productHandle,
                byReference is null ? subscriptionReference : null,
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for {Reference} on {Handle}.",
                created.Id, shopper.Reference, productHandle);
            return new SubscribeResult(created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var recovered = await FindLiveSubscriptionForPlanAsync(customer.Id, productHandle, cancellationToken)
                ?? await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (recovered is not null && recovered.IsLive)
            {
                return new SubscribeResult(recovered, Created: false);
            }

            throw;
        }
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListProductsForProductFamilyAsync(cancellationToken);
        return plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(shopper, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for {Reference}.",
                created.Id, shopper.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Unique reference collision from a concurrent create — look up the winner.
            var raced = await _maxio.ReadCustomerByReferenceAsync(shopper.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionForPlanAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s.IsLive &&
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    internal static string BuildSubscriptionReference(string customerReference, string productHandle)
        => $"{customerReference}:{productHandle}";
}
