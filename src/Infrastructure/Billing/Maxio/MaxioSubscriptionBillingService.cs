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
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollGates = new(StringComparer.Ordinal);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();

        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        string buyerId,
        string email,
        string? userName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        _options.EnsureConfigured();

        var plans = await ListAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = EnrollGates.GetOrAdd($"{buyerId}:{plan.Handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(buyerId, email, userName, cancellationToken);
            var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, buyerId, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation($"Returning existing Maxio subscription {existing.Id} for buyer {buyerId} on plan {plan.Handle}.");
                return ToShopperSubscription(existing);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscriptionBody
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = SubscriptionReference(buyerId, plan.Handle),
                        PaymentCollectionMethod = "remittance"
                    }
                }, cancellationToken);

                _logger.LogInformation($"Created Maxio subscription {created.Id} for buyer {buyerId} on plan {plan.Handle}.");
                return ToShopperSubscription(created);
            }
            catch (SubscriptionEnrollmentException)
            {
                var raced = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, buyerId, cancellationToken);
                if (raced is not null)
                {
                    return ToShopperSubscription(raced);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        _options.EnsureConfigured();

        var customer = await _maxio.ReadCustomerByReferenceAsync(CustomerReference(buyerId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string buyerId,
        string email,
        string? userName,
        CancellationToken cancellationToken)
    {
        var reference = CustomerReference(buyerId);
        var existing = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ShopperName.FromIdentity(email, userName);

        try
        {
            return await _maxio.CreateCustomerAsync(new CreateCustomerRequest
            {
                Customer = new CreateCustomerBody
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            }, cancellationToken);
        }
        catch (SubscriptionEnrollmentException)
        {
            // Unique `reference` is enforced by Maxio; a double-click can lose the create race.
            var createdByPeer = await _maxio.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByPeer is not null)
            {
                return createdByPeer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(
        int customerId,
        string planHandle,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var byReference = await _maxio.FindSubscriptionByReferenceAsync(
            SubscriptionReference(buyerId, planHandle),
            cancellationToken);
        if (byReference is not null && IsCurrent(byReference.State))
        {
            return byReference;
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            IsCurrent(subscription.State)
            && string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCurrent(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static string CustomerReference(string buyerId) => buyerId;

    private static string SubscriptionReference(string buyerId, string planHandle) => $"{buyerId}:{planHandle}";

    private static SubscriptionPlan ToPlan(MaxioProduct product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit ?? "month");

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? "Subscription",
            subscription.ProductPriceInCents != 0
                ? subscription.ProductPriceInCents
                : subscription.Product?.PriceInCents ?? 0,
            subscription.State ?? "unknown",
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

internal static class ShopperName
{
    private static readonly char[] NameSeparators = { '.', '-', '_', ' ' };

    public static (string FirstName, string LastName) FromIdentity(string email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName ?? "shopper";
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        var tokens = local
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .ToArray();

        var first = tokens.Length > 0 ? tokens[0] : "Shopper";
        var last = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        value.Length == 0
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
