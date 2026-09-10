using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements <see cref="IMaxioSubscriptionService"/> over the Maxio REST API. Owns the "ensure a
/// customer exists then enroll them" orchestration and keeps the whole flow idempotent on the
/// subscriber's reference.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioApiClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(FamilySelector, cancellationToken);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle = null,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);

        // 1. Ensure a Maxio customer exists for this eShopOnWeb user (idempotent on reference).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // 2. If a live subscription to this plan already exists, return it rather than creating another.
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var duplicate = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            MapSubscription(s).IsLive);
        if (duplicate is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} already has live subscription {SubscriptionId} to plan {PlanHandle}; returning it.",
                customer.Id, duplicate.Id, plan.Handle);
            return MapSubscription(duplicate);
        }

        // 3. Enroll the customer in the plan by its stable handle.
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
            }
        };

        var created = await _client.CreateSubscriptionAsync(request, cancellationToken);
        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
            created.Id, customer.Id, plan.Handle, created.State);

        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    /// <summary>
    /// Looks up the customer by reference and creates one if absent. Handles the double-click / race
    /// case: if a concurrent create already claimed the reference (Maxio rejects the duplicate), we
    /// re-read the existing customer instead of surfacing an error.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioException ex) when (ex.IsClientError)
        {
            // A concurrent request likely created the customer first; re-read by reference.
            var afterConflict = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (afterConflict is not null)
            {
                _logger.LogInformation(
                    "Customer create for reference {Reference} conflicted; using existing customer {CustomerId}.",
                    subscriber.Reference, afterConflict.Id);
                return afterConflict;
            }

            throw;
        }
    }

    /// <summary>
    /// Resolves the plan to subscribe to. When a handle is supplied it must exist in the configured
    /// family; when omitted, the highest-priced plan is used as a sensible, catalog-agnostic default.
    /// </summary>
    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new MaxioException(
                $"No subscription plans are available in product family '{_settings.ProductFamilyHandle}'.");
        }

        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            var match = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var available = string.Join(", ", plans.Select(p => p.Handle));
                throw new MaxioException(
                    $"Unknown plan '{planHandle}'. Available plans: {available}.", statusCode: 404);
            }

            return match;
        }

        var defaultPlan = plans.OrderByDescending(p => p.PriceInCents).First();
        _logger.LogInformation("No plan specified; defaulting to '{PlanHandle}'.", defaultPlan.Handle);
        return defaultPlan;
    }

    /// <summary>The family path segment: the configured handle, prefixed per the spec so it resolves by handle.</summary>
    private string FamilySelector => $"handle:{_settings.ProductFamilyHandle}";

    private SubscriptionPlan MapPlan(MaxioProduct product) => new(
        id: product.Id,
        handle: product.Handle ?? string.Empty,
        name: product.Name,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        requiresPaymentMethod: product.RequireCreditCard,
        productFamilyHandle: product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription)
    {
        var price = subscription.CurrentBillingAmountInCents
            ?? subscription.ProductPriceInCents
            ?? subscription.Product?.PriceInCents
            ?? 0;

        return new CustomerSubscription(
            id: subscription.Id,
            customerId: subscription.Customer?.Id ?? 0,
            customerReference: subscription.Customer?.Reference,
            state: subscription.State,
            planHandle: subscription.Product?.Handle,
            planName: subscription.Product?.Name,
            currentPriceInCents: price,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            createdAt: subscription.CreatedAt);
    }
}
