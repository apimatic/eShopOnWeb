using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Enrolls shoppers in Maxio-backed recurring plans. Customer and subscription
/// creation is idempotent so a double-click cannot create duplicates.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        ISubscriptionBillingGateway gateway,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _gateway.ListProductsForFamilyAsync(cancellationToken);
        return products
            .Where(product => string.IsNullOrEmpty(product.ArchivedAt) && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        Shopper shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var gate = SubscribeGates.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plan = await ResolvePlanAsync(productHandle, cancellationToken);
            var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(shopper.UserId, plan.Handle);

            var existingByReference = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference is not null && IsLive(existingByReference.State))
            {
                _logger.LogInformation("Returning existing subscription {SubscriptionId} for shopper {UserId} and plan {PlanHandle}.",
                    existingByReference.Id, shopper.UserId, plan.Handle);
                return ToCustomerSubscription(existingByReference);
            }

            var customerSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveMatch = customerSubscriptions.FirstOrDefault(subscription =>
                IsLive(subscription.State) &&
                string.Equals(subscription.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (liveMatch is not null)
            {
                _logger.LogInformation("Returning live subscription {SubscriptionId} for shopper {UserId} and plan {PlanHandle}.",
                    liveMatch.Id, shopper.UserId, plan.Handle);
                return ToCustomerSubscription(liveMatch);
            }

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for shopper {UserId} on plan {PlanHandle}.",
                    created.Id, shopper.UserId, plan.Handle);
                return ToCustomerSubscription(created);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await _gateway.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced is not null)
                {
                    return ToCustomerSubscription(raced);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(
        Shopper shopper,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(shopper, nameof(shopper));

        var customer = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(item =>
            string.Equals(item.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        return plan;
    }

    private async Task<BillingCustomer> GetOrCreateCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _gateway.CreateCustomerAsync(shopper, shopper.UserId, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper {UserId}.", created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _gateway.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle) =>
        $"{userId}:{productHandle}";

    internal static bool IsLive(string state) =>
        !TerminalSubscriptionStates.Contains(state);

    internal static (string FirstName, string LastName) SplitShopperName(Shopper shopper) =>
        ShopperNameFormatter.Split(shopper);

    private static SubscriptionPlan ToPlan(BillingProduct product) =>
        new()
        {
            Id = product.Id,
            Handle = product.Handle!,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = product.PriceInCents / 100m,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit
        };

    private static CustomerSubscription ToCustomerSubscription(BillingSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.ProductHandle ?? string.Empty,
            PlanName = subscription.ProductName,
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPriceInCents / 100m,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
}
