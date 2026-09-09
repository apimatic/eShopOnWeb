using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionService"/>. Orchestrates the find-or-create
/// customer and idempotent subscribe flows and maps Maxio wire models to domain models.
/// </summary>
internal class MaxioSubscriptionService : ISubscriptionService
{
    // Subscriptions in these end-of-life states no longer count as an active enrollment, so the
    // shopper is allowed to subscribe to the plan again. Every other state is treated as a live
    // subscription for de-duplication purposes.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        MaxioSettings settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken)
            .ConfigureAwait(false);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        // 1. Validate the requested plan exists in the configured family (also prevents subscribing
        //    to arbitrary products outside the family).
        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        // 2. Find-or-create the Maxio customer keyed on the eShopOnWeb user id (idempotent).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);

        // 3. If the shopper already has a live subscription to this plan, return it (idempotent hit).
        var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                $"Shopper (ref '{subscriber.Reference}', customer {customer.Id}) already subscribed to '{planHandle}' (subscription {existing.Id}); returning existing.");
            return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
        }

        // 4. Create the subscription. A deterministic uniqueness token lets Maxio reject a duplicate
        //    submission (409) within its 60-minute window, closing the double-click race.
        var input = new CreateSubscriptionInput
        {
            ProductHandle = planHandle,
            CustomerReference = customer.Reference ?? subscriber.Reference,
            PaymentCollectionMethod = "remittance"
        };
        var uniquenessToken = $"eshop-sub-{customer.Id}-{planHandle}";

        try
        {
            var created = await _client.CreateSubscriptionAsync(input, uniquenessToken, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                $"Created subscription {created.Id} to '{planHandle}' for customer {customer.Id} (ref '{subscriber.Reference}').");
            return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicate)
        {
            // Two near-simultaneous requests raced; the first won (Maxio rejected this one via the
            // uniqueness token). Resolve to the winning subscription, retrying briefly in case it is
            // not yet visible in the customer's subscription list.
            _logger.LogWarning(
                $"Duplicate subscribe detected for customer {customer.Id} / '{planHandle}'; resolving to the existing subscription.");
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken).ConfigureAwait(false);
                if (raced is not null)
                {
                    return new SubscribeResult(MapSubscription(raced), alreadyExisted: true);
                }

                await Task.Delay(150 * attempt, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    // Bounds the find-or-create retry loop that reconciles concurrent customer creation and Maxio's
    // read-after-write lag (a lookup can momentarily 404 right after another request created the row).
    private const int MaxEnsureCustomerAttempts = 4;

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var input = new CreateCustomerInput
        {
            FirstName = string.IsNullOrWhiteSpace(subscriber.FirstName) ? "eShopOnWeb" : subscriber.FirstName,
            LastName = string.IsNullOrWhiteSpace(subscriber.LastName) ? "Subscriber" : subscriber.LastName,
            Email = subscriber.Email,
            Reference = subscriber.Reference
        };

        for (var attempt = 1; ; attempt++)
        {
            var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            try
            {
                var created = await _client.CreateCustomerAsync(input, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation($"Created Maxio customer {created.Id} for eShopOnWeb user ref '{subscriber.Reference}'.");
                return created;
            }
            catch (MaxioApiException ex)
                when ((ex.StatusCode == HttpStatusCode.UnprocessableEntity || ex.IsDuplicate) && attempt < MaxEnsureCustomerAttempts)
            {
                // Another request created the customer first (reference must be unique). Back off briefly
                // to let the write become visible, then loop to re-read it.
                _logger.LogWarning($"Customer create for ref '{subscriber.Reference}' lost a race (attempt {attempt}); re-reading.");
                await Task.Delay(150 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken).ConfigureAwait(false);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(s.State) &&
            !TerminalStates.Contains(s.State!));
    }

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Id = product.Id,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = (int)product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription s)
    {
        var priceInCents = s.ProductPriceInCents > 0
            ? s.ProductPriceInCents
            : s.Product?.PriceInCents ?? 0;

        return new CustomerSubscription
        {
            Id = s.Id,
            State = s.State ?? string.Empty,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PriceInCents = (int)priceInCents,
            PaymentCollectionMethod = s.PaymentCollectionMethod,
            CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt
        };
    }
}
