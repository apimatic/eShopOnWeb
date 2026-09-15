using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which the shopper no longer has a usable subscription and may enroll again.
    /// All other states are treated as an existing enrollment (idempotent subscribe).
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingClient maxio,
        MaxioOptions options,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BillingSubscription> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.CustomerReference, nameof(request.CustomerReference));
        Guard.Against.NullOrWhiteSpace(request.Email, nameof(request.Email));
        Guard.Against.NullOrWhiteSpace(request.FirstName, nameof(request.FirstName));
        Guard.Against.NullOrWhiteSpace(request.LastName, nameof(request.LastName));
        Guard.Against.NullOrWhiteSpace(request.ProductHandle, nameof(request.ProductHandle));

        var productHandle = request.ProductHandle.Trim();
        await EnsureProductHandleBelongsToFamilyAsync(productHandle, cancellationToken);

        var gate = UserGates.GetOrAdd(request.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for customer reference {CustomerReference} on plan {ProductHandle}.",
                    existing.Id, request.CustomerReference, productHandle);
                return existing;
            }

            var subscriptionReference = BuildSubscriptionReference(request.CustomerReference, productHandle);
            var byReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (byReference is not null && !IsTerminal(byReference.State))
            {
                return byReference;
            }

            if (byReference is not null && IsTerminal(byReference.State))
            {
                subscriptionReference = $"{subscriptionReference}:{Guid.NewGuid():N}";
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id,
                    productHandle,
                    subscriptionReference,
                    paymentCollectionMethod: "remittance",
                    cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {ProductHandle}.",
                    created.Id, customer.Id, productHandle);
                return created;
            }
            catch (MaxioClientException ex) when (ex.StatusCode == 422)
            {
                var recovered = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken)
                    ?? await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null && !IsTerminal(recovered.State))
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

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));

        var customer = await _maxio.ReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task EnsureProductHandleBelongsToFamilyAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnknownSubscriptionPlanException(productHandle);
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var existing = await _maxio.ReadCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                request.FirstName,
                request.LastName,
                request.Email,
                request.CustomerReference,
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, request.CustomerReference);
            return created;
        }
        catch (MaxioClientException ex) when (ex.StatusCode == 422)
        {
            var raced = await _maxio.ReadCustomerByReferenceAsync(request.CustomerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<BillingSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !IsTerminal(s.State));
    }

    private static string BuildSubscriptionReference(string customerReference, string productHandle)
        => $"{customerReference}:{productHandle}";

    private static bool IsTerminal(string? state)
        => !string.IsNullOrWhiteSpace(state) && TerminalStates.Contains(state);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }
    }
}
