using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing.
/// The eShopOnWeb user id is used as the Maxio customer <c>reference</c>, so a customer
/// can be looked up deterministically and duplicate customers/subscriptions are never created.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscriptions in these states already occupy the plan; re-subscribing returns the
    // existing one. End-of-life states (canceled, expired, trial_ended, failed_to_create)
    // are excluded so a shopper can subscribe again after cancellation.
    private static readonly HashSet<string> OccupyingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "unpaid", "paused", "soft_failure", "on_hold", "awaiting_signup"
    };

    // The seeded plans require no payment method; remittance issues the invoice without
    // attempting a card charge, so signup succeeds without card capture / 3-DS.
    private const string CardlessCollectionMethod = "remittance";

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(IMaxioClient client, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userId, string email, string? displayName, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(userId, email, displayName, cancellationToken);

        // Idempotency: if the shopper already holds this plan in a live state, return it
        // instead of creating a second subscription (e.g. double-click / retried request).
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var current = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && OccupyingStates.Contains(s.State));
        if (current is not null)
        {
            _logger.LogInformation("User {UserId} already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning existing.",
                userId, plan.Handle, current.Id);
            return Map(current);
        }

        var created = await _client.CreateSubscriptionAsync(plan.Handle, userId, CardlessCollectionMethod, cancellationToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
            created.Id, userId, plan.Handle);
        return Map(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, string? displayName, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitName(displayName, email);
        try
        {
            return await _client.CreateCustomerAsync(firstName, lastName, email, userId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness race (concurrent first subscribe): the customer now exists.
            _logger.LogWarning("Customer create for reference {Reference} returned 422; re-looking up.", userId);
            var raced = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
            return raced ?? throw new MaxioApiException(ex.StatusCode, ex.Message);
        }
    }

    private static (string FirstName, string LastName) SplitName(string? displayName, string email)
    {
        var name = displayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = email.Split('@')[0];
        }
        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], "-");
    }

    private static CustomerSubscription Map(MaxioSubscription s) => new()
    {
        SubscriptionId = s.Id,
        State = s.State ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.Product?.PriceInCents ?? 0,
        Interval = s.Product?.Interval ?? 0,
        IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
        PaymentCollectionMethod = s.PaymentCollectionMethod ?? string.Empty,
        ActivatedAt = s.ActivatedAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt
    };
}
