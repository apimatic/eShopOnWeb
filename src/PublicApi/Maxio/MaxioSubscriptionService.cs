using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscription flows on top of Maxio Advanced Billing:
/// ensures a Maxio customer exists for the signed-in shopper (idempotent, keyed by
/// the shopper's username as the Maxio customer reference) and enrolls them into a
/// plan without ever creating duplicate customers or duplicate subscriptions.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Maxio subscription states that represent a live or in-trouble subscription;
    // end-of-life states (canceled, expired, trial_ended, ...) allow re-subscribing.
    private static readonly HashSet<string> BlockingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "assessing", "pending", "trialing", "awaiting_signup",
        "past_due", "soft_failure", "unpaid"
    };

    private readonly IMaxioApiClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    // Serializes concurrent subscribe attempts for the same user + plan
    // (e.g. a double-clicked button) so only one subscription is created.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioSubscriptionService(
        IMaxioApiClient maxioClient,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanSummary>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsForFamilyAsync(
            _options.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlanSummary(
                p.Handle!,
                p.Name,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit,
                p.RequireCreditCard))
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(
        string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(username, cancellationToken);

        var plans = await _maxioClient.ListProductsForFamilyAsync(
            _options.ProductFamilyHandle!, cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            p.ArchivedAt is null);

        if (plan is null)
        {
            throw new MaxioApiException(
                $"Subscription plan '{productHandle}' was not found in product family '{_options.ProductFamilyHandle}'.", 404);
        }

        var lockKey = $"{customer.Id}|{plan.Handle}";
        var subscriptionLock = _subscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            // Idempotency guard: if the customer already holds a live subscription
            // on this plan (e.g. the button was double-clicked), return it instead
            // of creating a second one.
            var existingSubscriptions = await _maxioClient
                .ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                BlockingStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe request for user {Username} returned existing Maxio subscription {SubscriptionId} (state {State}) instead of creating a duplicate.",
                    username, existing.Id, existing.State);
                return Map(existing);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                plan.Handle!, customer.Id, cancellationToken);

            _logger.LogInformation(
                "User {Username} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} created in state {State}.",
                username, plan.Handle, created.Id, created.State);

            return Map(created);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(
        string username, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(username, cancellationToken);
        var subscriptions = await _maxioClient
            .ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions.Select(Map).ToList();
    }

    /// <summary>
    /// Guarantees exactly one Maxio customer per eShopOnWeb user. The Maxio
    /// customer reference is the shopper's username; creation races are resolved
    /// because Maxio enforces reference uniqueness (422), in which case the
    /// existing customer is re-read and returned.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string username, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.GetCustomerByReferenceAsync(username, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(username);

        try
        {
            _logger.LogInformation(
                "Creating Maxio customer for eShopOnWeb user {Username}.", username);

            return await _maxioClient.CreateCustomerAsync(
                new MaxioCreateCustomerBody
                {
                    Reference = username,
                    Email = username,
                    FirstName = firstName,
                    LastName = lastName,
                    Organization = "eShopOnWeb"
                },
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a creation race (reference must be unique) - the winner's
            // customer is the one we want.
            var raced = await _maxioClient.GetCustomerByReferenceAsync(username, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer for {Username} already existed after a concurrent create; reusing customer {CustomerId}.",
                    username, raced.Id);
                return raced;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string username)
    {
        var at = username.IndexOf('@');
        var localPart = at > 0 ? username[..at] : username;
        var domainPart = at > 0 && at < username.Length - 1 ? username[(at + 1)..] : "Customer";

        var first = localPart.Split('.', '_', '-', '+')[0];
        if (string.IsNullOrWhiteSpace(first))
        {
            first = "eShop";
        }

        return (char.ToUpperInvariant(first[0]) + first[1..], domainPart);
    }

    private static SubscriptionSummary Map(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.State,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.Currency,
            subscription.Product?.IntervalUnit,
            subscription.Product?.Interval,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
}