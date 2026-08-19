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
/// Ensures a Maxio customer for each shopper and enrolls them in a plan without duplicating
/// customers or live subscriptions on a double-click.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioBillingGateway _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListPlansAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrWhiteSpace(request.UserId, nameof(request.UserId));
        Guard.Against.NullOrWhiteSpace(request.Email, nameof(request.Email));
        Guard.Against.NullOrWhiteSpace(request.FirstName, nameof(request.FirstName));
        Guard.Against.NullOrWhiteSpace(request.LastName, nameof(request.LastName));
        Guard.Against.NullOrWhiteSpace(request.ProductHandle, nameof(request.ProductHandle));

        var gateKey = $"{request.UserId}:{request.ProductHandle}";
        var gate = SubscribeGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveMatch = FindLiveSubscription(existing, request.ProductHandle);
            if (liveMatch is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.",
                    liveMatch.Id, request.UserId, request.ProductHandle);
                return liveMatch;
            }

            var uniquenessToken = $"eshop-sub:{request.UserId}:{request.ProductHandle}:{Guid.NewGuid():N}";
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(
                    customer.Id, request.ProductHandle, uniquenessToken, cancellationToken);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.",
                    created.Id, request.UserId, request.ProductHandle);
                return created;
            }
            catch (DuplicateException)
            {
                var afterConflict = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var recovered = FindLiveSubscription(afterConflict, request.ProductHandle)
                    ?? afterConflict.FirstOrDefault(s => ProductHandleEquals(s.ProductHandle, request.ProductHandle));
                if (recovered is not null)
                {
                    return recovered;
                }

                throw new BillingException(
                    $"A duplicate subscribe request was detected for plan '{request.ProductHandle}', but the existing subscription could not be loaded.",
                    409);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                request.UserId,
                request.Email,
                request.FirstName,
                request.LastName,
                uniquenessToken: $"eshop-customer:{request.UserId}",
                cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, request.UserId);
            return created;
        }
        catch (Exception ex) when (ex is DuplicateException or BillingException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            if (ex is BillingException)
            {
                throw;
            }

            throw new BillingException(
                "A Maxio customer for this user already exists, but it could not be loaded by reference.",
                ex,
                409);
        }
    }

    private static CustomerSubscription? FindLiveSubscription(
        IReadOnlyList<CustomerSubscription> subscriptions,
        string productHandle)
    {
        return subscriptions.FirstOrDefault(s =>
            ProductHandleEquals(s.ProductHandle, productHandle) && IsLive(s.State));
    }

    private static bool ProductHandleEquals(string? left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);
}
