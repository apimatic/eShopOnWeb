using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Idempotent subscribe orchestration. Maxio is the system of record: the eShopOnWeb
/// user id is stored as the Maxio customer <c>reference</c>, and each plan enrollment
/// uses a deterministic subscription <c>reference</c> plus a uniqueness token so a
/// double-click cannot create two customers or two subscriptions.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> CurrentStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due",
        "soft_failure", "unpaid", "paused", "awaiting_signup"
    };

    private readonly IMaxioBillingGateway _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioBillingGateway maxio, IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListPlansAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));
        Guard.Against.NullOrEmpty(shopper.UserId, nameof(shopper.UserId));

        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(shopper), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));
        Guard.Against.NullOrEmpty(shopper.UserId, nameof(shopper.UserId));
        Guard.Against.NullOrEmpty(shopper.Email, nameof(shopper.Email));

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required to subscribe.");
        }

        productHandle = productHandle.Trim();

        var gate = UserGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await RequirePlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);

            var existing = await FindCurrentEnrollmentAsync(shopper, customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                    existing.Id, shopper.UserId, plan.Handle);
                return existing;
            }

            var subscriptionReference = SubscriptionReference(shopper, plan.Handle);

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    new CreateBillingSubscription(plan.Handle, customer.Id, subscriptionReference, "remittance"),
                    Guid.NewGuid().ToString("D"),
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                    created.Id, shopper.UserId, plan.Handle);
                return created;
            }
            catch (BillingConflictException)
            {
                _logger.LogWarning("Duplicate Maxio subscription submission for user {UserId} plan {Plan}; recovering.",
                    shopper.UserId, plan.Handle);
                var recovered = await FindCurrentEnrollmentAsync(shopper, customer.Id, plan.Handle, cancellationToken);
                if (recovered is not null)
                {
                    return recovered;
                }

                throw;
            }
            catch (BillingValidationException)
            {
                var recovered = await FindCurrentEnrollmentAsync(shopper, customer.Id, plan.Handle, cancellationToken);
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

    private async Task<SubscriptionPlan> RequirePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingNotFoundException($"Subscription plan '{productHandle}' was not found.");
        }

        return plan;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(shopper);
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var names = SplitDisplayName(shopper);
        try
        {
            return await _maxio.CreateCustomerAsync(
                new CreateBillingCustomer(names.FirstName, names.LastName, shopper.Email, reference),
                $"eshop-customer-{Guid.NewGuid():D}",
                cancellationToken);
        }
        catch (BillingConflictException)
        {
            _logger.LogWarning("Duplicate Maxio customer submission for user {UserId}; recovering.", shopper.UserId);
        }
        catch (BillingValidationException)
        {
            _logger.LogWarning("Maxio customer create rejected for user {UserId}; attempting lookup.", shopper.UserId);
        }

        var recovered = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (recovered is not null)
        {
            return recovered;
        }

        throw new BillingConflictException("A Maxio customer could not be created or recovered for this user.");
    }

    private async Task<SubscriptionDetails?> FindCurrentEnrollmentAsync(
        ShopperIdentity shopper,
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            SubscriptionReference(shopper, productHandle), cancellationToken);
        if (byReference is not null && IsCurrent(byReference))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsCurrent(s) &&
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCurrent(SubscriptionDetails subscription) =>
        !string.IsNullOrWhiteSpace(subscription.State) && CurrentStates.Contains(subscription.State);

    internal static string CustomerReference(ShopperIdentity shopper) => shopper.UserId;

    internal static string SubscriptionReference(ShopperIdentity shopper, string productHandle) =>
        $"eshop:{shopper.UserId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = shopper.UserName;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = shopper.Email;
        }

        var local = source.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "eShopOnWeb");
    }
}
