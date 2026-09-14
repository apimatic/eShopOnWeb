using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing API. Maxio is the
/// system of record: nothing about plans, customers or subscriptions is persisted locally, it is
/// always read live from Maxio.
/// </summary>
internal class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Every subscription state except the terminal/never-started ones counts as "already enrolled"
    // for idempotency purposes - re-subscribing a canceled/expired/failed subscription should create
    // a fresh one, but re-subscribing an active/trialing/dunning one should just return it.
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due",
        "suspended", "paused", "unpaid", "on_hold", "awaiting_signup"
    };

    private readonly MaxioApiClient _client;
    private readonly IOptions<MaxioOptions> _options;
    private readonly AsyncKeyedLocker _locker;

    public MaxioSubscriptionBillingService(MaxioApiClient client, IOptions<MaxioOptions> options, AsyncKeyedLocker locker)
    {
        _client = client;
        _options = options;
        _locker = locker;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.Value.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<(Subscription Subscription, bool WasCreated)> SubscribeAsync(string buyerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        // Serialize per-buyer so a double-click can't slip both requests past the
        // "does an active subscription already exist" check below.
        using var _ = await _locker.AcquireAsync(buyerEmail.ToLowerInvariant(), cancellationToken);

        var customer = await EnsureCustomerAsync(buyerEmail, cancellationToken);

        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyEnrolled = existingSubscriptions.FirstOrDefault(s =>
            s.Product is not null &&
            string.Equals(s.Product.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            NonTerminalStates.Contains(s.State));

        if (alreadyEnrolled is not null)
        {
            return (MapSubscription(alreadyEnrolled), false);
        }

        var created = await _client.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        return (MapSubscription(created), true);
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForBuyerAsync(string buyerEmail, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buyerEmail);

        var customer = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<CustomerPayload> EnsureCustomerAsync(string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(buyerEmail);

        try
        {
            return await _client.CreateCustomerAsync(buyerEmail, firstName, lastName, reference: buyerEmail, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio has no upsert endpoint and enforces a unique "reference" per customer, so a 422
            // here most likely means a concurrent request (e.g. from another process/tab) created the
            // customer between our lookup and this create call. Re-fetch instead of failing the request.
            var recheck = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
            if (recheck is not null)
            {
                return recheck;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Customer");
    }

    private static SubscriptionPlan MapPlan(ProductPayload product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static Subscription MapSubscription(SubscriptionPayload subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        State = subscription.State,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
