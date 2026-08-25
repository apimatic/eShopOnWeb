using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing.
/// The eShopOnWeb user Id is used as the Maxio customer reference, which makes customer
/// creation idempotent; duplicate subscribes are short-circuited by returning the existing
/// live subscription for the same plan.
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription is considered "live" for idempotency purposes.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "past_due", "on_hold"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(MaxioApiClient client, IOptions<MaxioSettings> settings, ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetProductFamilyAsync(cancellationToken);
        var products = await _client.ListProductsAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);

        var existing = await _client.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var duplicate = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(s.State));

        if (duplicate != null)
        {
            _logger.LogInformation(
                "User {UserId} already has a live subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                userId, duplicate.Id, planHandle);
            return Map(duplicate);
        }

        var subscription = await _client.CreateSubscriptionAsync(
            customer.Id, planHandle, reference: $"{userId}:{planHandle}", cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.",
            subscription.Id, userId, planHandle);

        return Map(subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio is not configured: Maxio:ProductFamilyHandle is required.");
        }

        var family = await _client.GetProductFamilyByHandleAsync(_settings.ProductFamilyHandle, cancellationToken);
        return family ?? throw new MaxioApiException(HttpStatusCode.NotFound,
            $"Product family '{_settings.ProductFamilyHandle}' was not found on the Maxio site.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var firstName = email.Split('@')[0];
        try
        {
            return await _client.CreateCustomerAsync(firstName, "eShop", email, userId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent subscribe for the same user (reference already taken):
            // the customer now exists, so look it up instead of failing.
            var winner = await _client.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (winner != null)
            {
                return winner;
            }

            throw;
        }
    }

    private static CustomerSubscription Map(MaxioSubscription subscription)
    {
        return new CustomerSubscription
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CustomerId = subscription.Customer?.Id ?? 0
        };
    }
}
