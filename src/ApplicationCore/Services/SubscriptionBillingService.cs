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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListPlansAsync(cancellationToken);

    public async Task<SubscribeToPlanResult> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("A shopper user id is required.", nameof(request));
        }

        var gate = UserGates.GetOrAdd(request.UserId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<ShopperSubscription>();
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<SubscribeToPlanResult> SubscribeCoreAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListPlansAsync(cancellationToken);
        var plan = ResolvePlan(plans, request.ProductHandle);

        var customer = await GetOrCreateCustomerAsync(request, cancellationToken);
        var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                request.UserId, plan.Handle, existing.Id);
            return new SubscribeToPlanResult(existing, created: false);
        }

        var uniquenessToken = Guid.NewGuid().ToString("D");
        try
        {
            var created = await _maxio.CreateSubscriptionAsync(
                customer.Id, plan.Handle, uniquenessToken, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper {UserId} on plan {PlanHandle}.",
                created.Id, request.UserId, plan.Handle);
            return new SubscribeToPlanResult(created, created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            var replayed = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (replayed is not null)
            {
                return new SubscribeToPlanResult(replayed, created: false);
            }

            throw;
        }
    }

    private async Task<BillingCustomer> GetOrCreateCustomerAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(request.UserName, request.Email);
        var uniquenessToken = Guid.NewGuid().ToString("D");
        try
        {
            var created = await _maxio.CreateCustomerAsync(
                request.UserId,
                firstName,
                lastName,
                request.Email,
                uniquenessToken,
                cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for shopper {UserId}.",
                created.Id, request.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<ShopperSubscription?> FindCurrentSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s.IsCurrent()
            && string.Equals(s.PlanHandle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static SubscriptionPlan ResolvePlan(IReadOnlyList<SubscriptionPlan> plans, string? productHandle)
    {
        if (plans.Count == 0)
        {
            throw new SubscriptionPlanNotFoundException(productHandle ?? "(none configured)");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return plans[0];
        }

        var match = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return match;
    }

    private static (string FirstName, string LastName) SplitName(string userName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var local = source;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            local = source[..at];
        }

        if (string.IsNullOrWhiteSpace(local))
        {
            return ("Shopper", "eShopOnWeb");
        }

        return (local, "eShopOnWeb");
    }
}
