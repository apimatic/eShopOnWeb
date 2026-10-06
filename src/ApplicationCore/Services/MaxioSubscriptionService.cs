using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the "Subscribe" capability against Maxio Advanced Billing,
/// which is the system of record. Enforces idempotency so a double-click never
/// creates two customers or two live subscriptions.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new();

    public MaxioSubscriptionService(
        IMaxioBillingClient client,
        MaxioSettings settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeOutcome> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var gate = ShopperLocks.GetOrAdd(customer.Reference ?? shopper.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindLiveSubscriptionForPlanAsync(customer, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation($"Shopper {customer.Reference} already subscribed to '{planHandle}'; returning existing subscription {existing.Id}.");
                return new SubscribeOutcome(existing, alreadySubscribed: true);
            }

            await ValidatePlanInCatalogAsync(planHandle, cancellationToken);

            var baseReference = BuildSubscriptionReference(customer.Reference, planHandle);
            try
            {
                var created = await _client.CreateSubscriptionAsync(new MaxioNewSubscriptionRequest
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = baseReference,
                    PaymentCollectionMethod = _settings.PaymentCollectionMethod
                }, cancellationToken);

                _logger.LogInformation($"Created subscription {created.Id} (state: {created.State}) for shopper {customer.Reference} on plan '{planHandle}'.");
                return new SubscribeOutcome(created, alreadySubscribed: false);
            }
            catch (MaxioApiException ex) when (ex.IsReferenceUniquenessViolation())
            {
                // Maxio enforces subscription-reference uniqueness. If the base reference is
                // already taken we have two cases to disambiguate:
                var afterRace = await FindLiveSubscriptionForPlanAsync(customer, planHandle, cancellationToken);
                if (afterRace is not null)
                {
                    // (a) a concurrent double-click won the race — adopt its live subscription.
                    _logger.LogInformation($"Concurrent subscribe detected for {customer.Reference} on '{planHandle}'; adopting subscription {afterRace.Id}.");
                    return new SubscribeOutcome(afterRace, alreadySubscribed: true);
                }

                // (b) a prior (now canceled) subscription still holds the reference —
                //     this is a genuine re-subscribe, so mint a unique reference and retry.
                var resubscribeReference = $"{baseReference}-at{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var reactivated = await _client.CreateSubscriptionAsync(new MaxioNewSubscriptionRequest
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = resubscribeReference,
                        PaymentCollectionMethod = _settings.PaymentCollectionMethod
                }, cancellationToken);

                _logger.LogInformation($"Re-subscribed shopper {customer.Reference} to '{planHandle}'; created subscription {reactivated.Id} (state: {reactivated.State}).");
                return new SubscribeOutcome(reactivated, alreadySubscribed: false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForShopperAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
        if (customer is null)
            return Array.Empty<MaxioSubscription>();

        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var found = await _client.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
        if (found is not null)
            return found;

        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioNewCustomerRequest
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.CustomerReference,
                Organization = "eShopOnWeb"
            }, cancellationToken);

            _logger.LogInformation($"Created Maxio customer {created.Id} for shopper {shopper.CustomerReference}.");
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceUniquenessViolation())
        {
            var raced = await _client.FindCustomerByReferenceAsync(shopper.CustomerReference, cancellationToken);
            if (raced is null) throw;

            _logger.LogInformation($"Maxio customer for {shopper.CustomerReference} already existed (race); continuing with {raced.Id}.");
            return raced;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(MaxioCustomer customer, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !MaxioSubscriptionStates.IsEndOfLife(s.State));
    }

    private async Task ValidatePlanInCatalogAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var match = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new SubscriptionPlanNotFoundException(planHandle);
    }

    private static string BuildSubscriptionReference(string? customerReference, string planHandle)
        => $"{customerReference}:{planHandle}";
}
