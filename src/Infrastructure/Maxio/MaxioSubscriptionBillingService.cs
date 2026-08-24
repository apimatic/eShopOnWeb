using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing against Maxio Advanced Billing, the billing system of record.
/// The eShopOnWeb user id is stored as the Maxio customer <c>reference</c>, which the spec
/// guarantees unique — that plus the lookup endpoint is what makes subscribe idempotent.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which an existing subscription to the same plan satisfies a subscribe request.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "awaiting_signup", "past_due", "on_hold"
    };

    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _settings.Validate();
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(ToPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<ShopperSubscription> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var current = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));
        if (current is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already has subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                shopper.UserId, current.Id, planHandle);
            return ToShopperSubscription(current);
        }

        var created = await _client.CreateSubscriptionAsync(planHandle, customer.Id, cancellationToken);
        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for shopper {UserId} on plan {PlanHandle}.",
            created.Id, shopper.UserId, planHandle);
        return ToShopperSubscription(created);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
            return Array.Empty<ShopperSubscription>();

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToShopperSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is not null)
            return customer;

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = shopper.FirstName,
                LastName = shopper.LastName,
                Email = shopper.Email,
                Reference = shopper.UserId
            }, cancellationToken);
        }
        catch (BillingIntegrationException ex) when (ex.StatusCode == (HttpStatusCode)422)
        {
            // Lost a race with a concurrent subscribe for the same shopper (reference is unique
            // per the spec) — the customer now exists, so read it back.
            _logger.LogWarning("Customer create for shopper {UserId} raced; re-reading by reference.", shopper.UserId);
            customer = await _client.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (customer is not null)
                return customer;
            throw;
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static ShopperSubscription ToShopperSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        Currency = subscription.Currency ?? string.Empty,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
