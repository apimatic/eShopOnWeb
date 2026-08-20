using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new(StringComparer.Ordinal);

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient maxio,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListProductsForFamilyAsync(_maxio.ProductFamilyHandle, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(
        BillingShopper shopper,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        BillingShopper shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("productHandle is required.", 400);
        }

        var handle = productHandle.Trim();
        var gate = ShopperLocks.GetOrAdd(shopper.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            if (plans.All(p => !string.Equals(p.Handle, handle, StringComparison.Ordinal)))
            {
                throw new BillingException(
                    $"Unknown subscription plan '{handle}'. List plans via GET /api/subscription-plans.",
                    400);
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var open = existing.FirstOrDefault(s =>
                s.IsOpen && string.Equals(s.ProductHandle, handle, StringComparison.Ordinal));
            if (open is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for {Reference} / {Handle}",
                    open.Id, shopper.CustomerReference, handle);
                return open;
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, handle, cancellationToken);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for {Reference} / {Handle} in state {State}",
                    created.Id, shopper.CustomerReference, handle, created.State);
                return created;
            }
            catch (BillingException) when (!cancellationToken.IsCancellationRequested)
            {
                // Race: a parallel click may have created the same plan. Re-read and return it.
                var retry = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var recovered = retry.FirstOrDefault(s =>
                    s.IsOpen && string.Equals(s.ProductHandle, handle, StringComparison.Ordinal));
                if (recovered is not null)
                {
                    return recovered;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(shopper, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for {Reference}",
                created.Id, shopper.CustomerReference);
            return created;
        }
        catch (BillingException ex) when (ex.StatusCode is 400 or 409 or (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }
}
