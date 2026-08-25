using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// ISubscriptionBillingService backed by Maxio Advanced Billing. The eShopOnWeb user id is
/// stored as the Maxio customer "reference", which the spec guarantees is unique per
/// customer — that is the anchor for idempotent customer creation.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription already entitles the shopper; re-subscribing to the
    // same plan returns the existing subscription instead of creating a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "awaiting_signup"
    };

    private readonly MaxioHttpClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioHttpClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForProductFamilyAsync(
            _settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlan
            {
                ProductId = p.Id,
                Name = p.Name ?? string.Empty,
                Handle = p.Handle ?? string.Empty,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty,
                RequiresCreditCard = p.RequireCreditCard
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string buyerId, string buyerEmail,
        string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var customer = await EnsureCustomerAsync(buyerId, buyerEmail, cancellationToken);

        // Idempotency: an existing live subscription to the same plan satisfies the request.
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var match = existing.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State != null && LiveStates.Contains(s.State));

        if (match != null)
        {
            _logger.LogInformation(
                "Buyer {BuyerId} already has subscription {SubscriptionId} for plan {PlanHandle}; returning existing.",
                buyerId, match.Id, productHandle);
            return Map(match);
        }

        try
        {
            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                // Subscription references must be unique in Maxio; keying by buyer+plan keeps
                // the value deterministic for the idempotency check above.
                Reference = $"{buyerId}:{productHandle}",
                // Remittance (invoice) collection: the seeded plans require no payment
                // method, so signup must not attempt an automatic card charge.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
            return Map(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent subscribe for the same plan — return the winner.
            var afterRace = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var concurrent = afterRace.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State != null && LiveStates.Contains(s.State));
            if (concurrent != null)
            {
                return Map(concurrent);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string buyerId,
        string buyerEmail, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string buyerId, string buyerEmail,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var localPart = buyerEmail.Split('@')[0];
        var createRequest = new MaxioCreateCustomer
        {
            FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart,
            LastName = "Shopper",
            Email = buyerEmail,
            Reference = buyerId
        };

        try
        {
            return await _client.CreateCustomerAsync(createRequest, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness is enforced by Maxio; a 422 here means a concurrent
            // request created the customer first. Re-read it.
            var raced = await _client.FindCustomerByReferenceAsync(buyerId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }
            throw;
        }
    }

    private static SubscriptionDetails Map(MaxioSubscription s)
    {
        return new SubscriptionDetails
        {
            SubscriptionId = s.Id,
            CustomerId = s.Customer?.Id ?? 0,
            State = s.State ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PriceInCents = s.Product?.PriceInCents ?? s.ProductPriceInCents,
            Interval = s.Product?.Interval ?? 0,
            IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
            ActivatedAt = s.ActivatedAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            BalanceInCents = s.BalanceInCents
        };
    }
}
