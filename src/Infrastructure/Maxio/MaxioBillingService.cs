using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow on top of <see cref="MaxioApiClient"/>, keeping Maxio as the
/// system of record. Responsible for idempotency: a user maps to exactly one Maxio customer
/// (keyed by reference) and a repeated subscribe to the same plan returns the existing subscription.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // States in which a subscription is considered dead, so a new subscribe is allowed.
    // Any other state counts as a live subscription that must not be duplicated.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioApiClient client,
        MaxioSettings settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserReference))
        {
            throw new ArgumentException("A user reference is required to subscribe.", nameof(request));
        }

        // Resolve the target plan against the live catalog so we never subscribe to a product
        // outside the configured family, and so we can echo plan details back to the caller.
        var plans = await GetPlansAsync(cancellationToken);
        var targetPlan = ResolveTargetPlan(plans, request.PlanHandle);

        // 1. Ensure a Maxio customer exists for this user (idempotent on the reference).
        var customer = await EnsureCustomerAsync(request, cancellationToken);

        // 2. If a live subscription to this plan already exists, return it instead of creating a duplicate.
        var existing = await FindLiveSubscriptionAsync(customer.Id, targetPlan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                $"Reusing existing subscription {existing.Id} for customer {customer.Id} on plan {targetPlan.Handle}.");
            return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
        }

        // 3. Create the subscription. These plans require no stored payment method, so enroll on
        //    invoice billing (remittance) — the subscription activates without card capture / 3-DS.
        var created = await _client.CreateSubscriptionAsync(
            new CreateSubscriptionWire
            {
                ProductHandle = targetPlan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance"
            },
            cancellationToken);

        _logger.LogInformation(
            $"Created subscription {created.Id} for customer {customer.Id} on plan {targetPlan.Handle}.");
        return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Array.Empty<CustomerSubscription>();
        }

        var customer = await _client.LookupCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private SubscriptionPlan ResolveTargetPlan(IReadOnlyList<SubscriptionPlan> plans, string? requestedHandle)
    {
        if (!string.IsNullOrWhiteSpace(requestedHandle))
        {
            var match = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new SubscriptionPlanNotFoundException(requestedHandle);
            }

            return match;
        }

        // No plan specified: fall back to the first plan in the family (catalog-agnostic default).
        var fallback = plans.FirstOrDefault();
        if (fallback is null)
        {
            throw new SubscriptionPlanNotFoundException("(default)");
        }

        return fallback;
    }

    private async Task<CustomerWire> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(request.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var attributes = new CustomerAttributesWire
        {
            Reference = request.UserReference,
            Email = string.IsNullOrWhiteSpace(request.Email) ? request.UserReference : request.Email,
            FirstName = FallbackFirstName(request),
            LastName = FallbackLastName(request)
        };

        try
        {
            return await _client.CreateCustomerAsync(attributes, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request likely created the customer first: the reference is unique in
            // Maxio, so re-look it up rather than surfacing the conflict.
            _logger.LogWarning(
                $"Create-customer returned 422 for reference '{request.UserReference}'; re-looking up existing customer.");
            var raced = await _client.LookupCustomerByReferenceAsync(request.UserReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<SubscriptionWire?> FindLiveSubscriptionAsync(
        int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && IsLive(s.State));
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private SubscriptionPlan MapPlan(ProductWire product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(SubscriptionWire subscription) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.Customer?.Id ?? 0,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static string FallbackFirstName(SubscribeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            return request.FirstName!;
        }

        // Derive a reasonable first name from the email local-part when none is available.
        var source = string.IsNullOrWhiteSpace(request.Email) ? request.UserReference : request.Email;
        var localPart = source.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }

    private static string FallbackLastName(SubscribeRequest request) =>
        string.IsNullOrWhiteSpace(request.LastName) ? "Subscriber" : request.LastName!;
}
