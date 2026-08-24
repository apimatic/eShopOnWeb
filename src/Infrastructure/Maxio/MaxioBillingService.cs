using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing against Maxio Advanced Billing, which is
/// the system of record for customers, plans, and subscriptions. All
/// operations are idempotent: the Maxio customer is keyed by the eShopOnWeb
/// user id via the customer reference, and a subscription is only created
/// when the customer has no live subscription to the same plan.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // States in which an existing subscription to the same plan is returned
    // instead of creating a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "past_due", "unpaid", "on_hold", "pending"
    };

    private readonly MaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioClient client, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var family = await _client.GetProductFamilyByHandleAsync(_settings.ProductFamilyHandle, cancellationToken);
        var products = await _client.ListProductsAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string customerReference, string email, string displayName, string planHandle, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await EnsureCustomerAsync(customerReference, email, displayName, cancellationToken);

        var existing = await _client.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
        var match = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(s.State));

        if (match is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} already has a {State} subscription {SubscriptionId} to plan {PlanHandle}; returning it instead of creating a duplicate.",
                customer.Id, match.State, match.Id, planHandle);
            return Map(match);
        }

        var created = await _client.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, customer.Id, planHandle);
        return Map(created);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _client.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomerDto> EnsureCustomerAsync(string reference, string email, string displayName, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            // eShopOnWeb identities are email-only; Maxio requires first/last name.
            return await _client.CreateCustomerAsync(reference, email, displayName, "(eShopOnWeb)", cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create (e.g. double-click): the
            // reference is unique in Maxio, so re-read the winning customer.
            var winner = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private static SubscriptionDetails Map(MaxioSubscriptionDto subscription)
    {
        return new SubscriptionDetails
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt
        };
    }
}
