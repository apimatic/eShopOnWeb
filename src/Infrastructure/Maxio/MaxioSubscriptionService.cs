using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the Subscribe hero flow against Maxio Advanced Billing: ensure a Maxio customer
/// exists for the eShopOnWeb user (idempotent), then enroll them in a plan (idempotent).
/// </summary>
internal class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that mean "this enrollment is over" - anything else is a live
    // enrollment we should not duplicate. See Maxio's subscription-states documentation.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient client, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrEmpty(p.Handle))
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userReference, string userEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await ResolveOrCreateCustomerAsync(userReference, userEmail, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserReference} already has a live Maxio subscription {SubscriptionId} to plan {PlanHandle}; not creating a duplicate.",
                userReference, existing.Id, planHandle);
            return MapSubscription(existing);
        }

        // These demo plans require no payment method. Maxio still defaults new subscriptions
        // to "automatic" collection (auto-charge a card), which fails signup when no card is
        // on file. Ask the site whether it uses Relationship Invoicing and pick the matching
        // no-card collection method instead of assuming one architecture.
        var site = await _client.GetSiteAsync(cancellationToken);
        var paymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";

        var createPayload = new CreateSubscriptionPayload
        {
            CustomerId = customer.Id,
            ProductHandle = planHandle,
            PaymentCollectionMethod = paymentCollectionMethod
        };
        var uniquenessToken = $"eshoponweb:subscription:{userReference}:{planHandle}";

        try
        {
            var created = await _client.CreateSubscriptionAsync(createPayload, uniquenessToken, cancellationToken);
            return MapSubscription(created);
        }
        catch (MaxioDuplicateRequestException)
        {
            // A concurrent request (e.g. a double-click) already created this subscription.
            var match = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (match is not null)
            {
                return MapSubscription(match);
            }

            throw new MaxioApiException("Maxio rejected the subscription request as a duplicate, but no matching subscription could be found.");
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<CustomerPayload> ResolveOrCreateCustomerAsync(string userReference, string userEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNameFromEmail(userEmail);
        var payload = new CreateCustomerPayload
        {
            Reference = userReference,
            Email = userEmail,
            FirstName = firstName,
            LastName = lastName
        };
        var uniquenessToken = $"eshoponweb:customer:{userReference}";

        try
        {
            var created = await _client.CreateCustomerAsync(payload, uniquenessToken, cancellationToken);
            _logger.LogInformation("Created Maxio customer {MaxioCustomerId} for user {UserReference}.", created.Id, userReference);
            return created;
        }
        catch (MaxioDuplicateRequestException)
        {
            // A concurrent request already created this customer.
            var match = await _client.FindCustomerByReferenceAsync(userReference, cancellationToken);
            return match ?? throw new MaxioApiException("Maxio rejected the customer request as a duplicate, but no matching customer could be found.");
        }
    }

    private async Task<SubscriptionPayload?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !EndOfLifeStates.Contains(s.State));
    }

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Subscriber");
    }

    private static SubscriptionPlan MapPlan(ProductPayload product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static CustomerSubscription MapSubscription(SubscriptionPayload subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
