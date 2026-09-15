using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListPlansAsync(cancellationToken);

    public async Task<(CustomerSubscription Subscription, bool Created)> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.UserId);
        Guard.Against.NullOrWhiteSpace(productHandle);

        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new ArgumentException($"Unknown subscription plan '{productHandle}'.", nameof(productHandle));
        }

        var gate = SubscribeGates.GetOrAdd(
            $"{shopper.UserId}:{plan.Handle}",
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, plan, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper);
        Guard.Against.NullOrWhiteSpace(shopper.UserId);

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<(CustomerSubscription Subscription, bool Created)> SubscribeCoreAsync(
        ShopperIdentity shopper,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = FindLiveForPlan(existing, plan.Handle);
        if (live is not null)
        {
            _logger.LogInformation(
                "Returning existing Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}",
                live.Id, shopper.UserId, plan.Handle);
            return (live, false);
        }

        var subscriptionReference = BuildSubscriptionReference(shopper.UserId, plan.Handle);
        var uniquenessToken = DeterministicToken($"subscription:{shopper.UserId}:{plan.Handle}");

        try
        {
            return (await CreateSubscriptionAsync(
                customer.Id, plan.Handle, subscriptionReference, uniquenessToken, shopper.UserId, cancellationToken), true);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var afterConflict = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var recovered = FindLiveForPlan(afterConflict, plan.Handle)
                            ?? await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    "Recovered Maxio subscription {SubscriptionId} after {StatusCode} for user {UserId} on plan {PlanHandle}",
                    recovered.Id, ex.StatusCode, shopper.UserId, plan.Handle);
                return (recovered, false);
            }

            if (ex.StatusCode == 409)
            {
                // A prior uniqueness_token may have been consumed by a 422. Retry once with a fresh token.
                return (await CreateSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    subscriptionReference,
                    Guid.NewGuid().ToString("N"),
                    shopper.UserId,
                    cancellationToken), true);
            }

            throw;
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        string uniquenessToken,
        string userId,
        CancellationToken cancellationToken)
    {
        var created = await _maxio.CreateSubscriptionAsync(
            customerId,
            productHandle,
            subscriptionReference,
            uniquenessToken,
            cancellationToken);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}",
            created.Id, userId, productHandle);
        return created;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var uniquenessToken = DeterministicToken($"customer:{shopper.UserId}");
        try
        {
            var created = await _maxio.CreateCustomerAsync(shopper, uniquenessToken, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is 409 or 422)
        {
            var recovered = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    "Recovered Maxio customer {CustomerId} after {StatusCode} for user {UserId}",
                    recovered.Id, ex.StatusCode, shopper.UserId);
                return recovered;
            }

            throw;
        }
    }

    private static CustomerSubscription? FindLiveForPlan(
        IReadOnlyList<CustomerSubscription> subscriptions,
        string productHandle)
    {
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !TerminalStates.Contains(s.State));
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    internal static string DeterministicToken(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash);
    }
}
