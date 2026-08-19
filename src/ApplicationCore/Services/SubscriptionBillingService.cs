using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "on_hold",
        "suspended",
        "trial_ended"
    };

    private readonly IMaxioBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListPlansAsync(cancellationToken);

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.UserId, nameof(shopper.UserId));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var handle = productHandle.Trim();
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new ArgumentException(
                $"Unknown subscription plan '{handle}'. List available plans from GET api/subscription-plans.",
                nameof(productHandle));
        }

        var gateKey = $"{shopper.UserId}:{plan.Handle}";
        var gate = _gates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {Handle}.",
                    existing.SubscriptionId, shopper.UserId, plan.Handle);
                return existing;
            }

            var reference = BuildSubscriptionReference(shopper.UserId, plan.Handle);
            var byReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (byReference is not null && IsLive(byReference.State))
            {
                return byReference;
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id, plan.Handle, reference, cancellationToken);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {Handle}.",
                    created.SubscriptionId, shopper.UserId, plan.Handle);
                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                            ?? await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    _logger.LogWarning(
                        "Create subscription conflict for user {UserId} plan {Handle}; returning existing subscription {SubscriptionId}.",
                        shopper.UserId, plan.Handle, raced.SubscriptionId);
                    return raced;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var customer = await _maxio.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(shopper, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                _logger.LogWarning(
                    "Create customer conflict for user {UserId}; returning existing customer {CustomerId}.",
                    shopper.UserId, raced.Id);
                return raced;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            IsLive(s.State));
    }

    public static string BuildSubscriptionReference(string userId, string productHandle)
        => $"eshop:{userId}:{productHandle}";

    public static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state);
}
