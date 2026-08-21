using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates Maxio as the billing system of record. Customer and subscription
/// creates are idempotent: Maxio unique <c>reference</c> values plus a process-local
/// gate absorb double-clicks without creating duplicates.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Live / problem states still represent an enrolled subscription. End-of-life
    // states (canceled, expired, ...) are allowed to enroll again.
    // Source: Maxio Subscription.State documentation (ab-dotnet-sdk 9.1.0).
    private static readonly HashSet<string> EnrolledStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListFamilyProductsAsync(cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (shopper is null)
        {
            throw new BillingException(400, "A shopper identity is required.");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A productHandle is required to subscribe.");
        }

        productHandle = productHandle.Trim();
        await EnsurePlanIsInConfiguredFamily(productHandle, cancellationToken);

        var gateKey = $"{shopper.UserId}:{productHandle}";
        var gate = SubscribeGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var enrolled = await FindEnrolledSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (enrolled is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} / {ProductHandle}.", enrolled.Id, shopper.UserId, productHandle);
                return new SubscribeResult(enrolled, created: false);
            }

            var reference = shopper.SubscriptionReference(productHandle);
            var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (byReference is not null && IsEnrolled(byReference.State))
            {
                return new SubscribeResult(byReference, created: false);
            }

            // Reference is unique site-wide. A canceled enrollment keeps the old
            // reference, so a later re-subscribe must use a new one.
            if (byReference is not null)
            {
                reference = $"{reference}:{Guid.NewGuid():N}";
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, reference, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} / {ProductHandle}.", created.Id, shopper.UserId, productHandle);
                return new SubscribeResult(created, created: true);
            }
            catch (BillingException ex) when (ex.StatusCode == 422)
            {
                var recovered = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken)
                    ?? await FindEnrolledSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                if (recovered is not null && IsEnrolled(recovered.State))
                {
                    _logger.LogInformation("Recovered Maxio subscription {SubscriptionId} after a 422 create for user {UserId}.", recovered.Id, shopper.UserId);
                    return new SubscribeResult(recovered, created: false);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(400, "A user id is required.");
        }

        var customer = await _maxio.FindCustomerByReferenceAsync($"eshop:{userId}", cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task EnsurePlanIsInConfiguredFamily(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _maxio.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.IsArchived)
        {
            throw new BillingException(400, $"Plan '{productHandle}' was not found.");
        }

        if (!string.Equals(product.ProductFamilyHandle, _maxio.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(400, $"Plan '{productHandle}' is not part of the configured product family.");
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                shopper.FirstName,
                shopper.LastName,
                shopper.Email,
                shopper.CustomerReference,
                cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (BillingException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<BillingSubscription?> FindEnrolledSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsEnrolled(s.State));
    }

    internal static bool IsEnrolled(string? state) =>
        !string.IsNullOrWhiteSpace(state) && EnrolledStates.Contains(state);
}
