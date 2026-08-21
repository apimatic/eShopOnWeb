using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates plan listing and idempotent subscribe against Maxio Advanced Billing.
/// Customer uniqueness uses the eShopOnWeb user id as Maxio <c>reference</c>;
/// double-click subscribe is serialized per shopper+plan and reuses any live subscription.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioAdvancedBillingGateway _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new(StringComparer.Ordinal);

    public SubscriptionBillingService(
        IMaxioAdvancedBillingGateway maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return _maxio.ListConfiguredFamilyProductsAsync(cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new SubscriptionBillingException("A signed-in shopper is required to subscribe.");
        }

        var handle = (productHandle ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(handle))
        {
            throw new SubscriptionBillingException("productHandle is required.");
        }

        var plans = await _maxio.ListConfiguredFamilyProductsAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionBillingException(
                $"Product handle '{handle}' is not a plan in the configured product family.",
                (int)HttpStatusCode.NotFound);
        }

        var lockKey = $"{shopper.UserId}:{handle.ToLowerInvariant()}";
        var gate = _subscribeLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, handle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("Reusing live Maxio subscription {SubscriptionId} for user {UserId} on {Handle}.",
                    existing.Id, shopper.UserId, handle);
                return existing;
            }

            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, handle);
            var uniquenessToken = Guid.NewGuid().ToString("D");

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id, handle, subscriptionReference, uniquenessToken, cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on {Handle}.",
                    created.Id, shopper.UserId, handle);
                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict
                                               || ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                var recovered = await RecoverDuplicateSubscribeAsync(
                    customer.Id, handle, subscriptionReference, cancellationToken);
                if (recovered != null)
                {
                    _logger.LogInformation(
                        "Recovered existing Maxio subscription {SubscriptionId} after {Status} for user {UserId} on {Handle}.",
                        recovered.Id, ex.StatusCode, shopper.UserId, handle);
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

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shopper);
        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            throw new SubscriptionBillingException("A signed-in shopper is required to list subscriptions.");
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper);
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                shopper.UserId, firstName, lastName, shopper.Email, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced != null)
            {
                _logger.LogInformation("Reused Maxio customer {CustomerId} for user {UserId} after create race.",
                    raced.Id, shopper.UserId);
                return raced;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));
    }

    private async Task<CustomerSubscription?> RecoverDuplicateSubscribeAsync(
        int customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var live = await FindLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        if (live != null)
        {
            return live;
        }

        var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference != null && IsLive(byReference.State))
        {
            return byReference;
        }

        return null;
    }

    internal static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state);

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"eshop:{userId}:{productHandle}";

    internal static (string FirstName, string LastName) SplitDisplayName(ShopperIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var local = source.Contains('@', StringComparison.Ordinal)
            ? source[..source.IndexOf('@')]
            : source;

        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        var first = parts.Length == 1 ? Capitalize(parts[0]) : "Shopper";
        return (first, "Customer");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    }
}
