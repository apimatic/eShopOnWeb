using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing adapter for <see cref="IBillingGateway"/>. Translates between the domain's
/// subscription model and the wire contracts declared in <c>maxio-spec/openapi.yaml</c>, and owns the
/// shape of every provider-side reference this application writes.
/// </summary>
public class MaxioBillingGateway : IBillingGateway
{
    private readonly IMaxioApiClient _client;
    private readonly MaxioSiteCache _siteCache;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        IMaxioApiClient client,
        MaxioSiteCache siteCache,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _siteCache = siteCache;
        _options = options.Value;
        _logger = logger;
    }

    public string ProductFamilyHandle => _options.ProductFamilyHandle ?? string.Empty;

    public string? DefaultPlanHandle => string.IsNullOrWhiteSpace(_options.DefaultPlanHandle) ? null : _options.DefaultPlanHandle;

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _client.ListProductsForProductFamilyAsync(
            ProductFamilyPathValue(),
            includeArchived: false,
            cancellationToken);

        var site = await GetSiteAsync(cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p!.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p!, site.Currency))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BillingCustomer?> FindCustomerAsync(string userKey, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var response = await _client.ReadCustomerByReferenceAsync(CustomerReference(userKey), cancellationToken);
        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(SubscriberProfile subscriber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var reference = CustomerReference(subscriber.UserKey);

        var existing = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (existing?.Customer is not null)
        {
            return MapCustomer(existing.Customer);
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Organization = subscriber.Organization,
                Reference = reference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            if (created.Customer is null)
            {
                throw new MaxioApiException("createCustomer", 200, new[] { "The provider did not return a customer." });
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Customer.Id, reference);
            return MapCustomer(created.Customer);
        }
        catch (MaxioApiException ex) when (ex.IsRequestRejected)
        {
            // Maxio enforces one customer per reference, so a rejected create here is most likely a
            // second request that lost a race with the first. Re-read before giving up: if the
            // customer now exists, the caller gets the same record either way.
            var raced = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (raced?.Customer is not null)
            {
                _logger.LogInformation(
                    "Customer create for reference {Reference} was rejected but the customer exists ({CustomerId}); treating as a concurrent create.",
                    reference,
                    raced.Customer.Id);
                return MapCustomer(raced.Customer);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var responses = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    public async Task<CustomerSubscription?> FindSubscriptionAsync(string userKey, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var response = await _client.FindSubscriptionAsync(SubscriptionReference(userKey, idempotencyKey), cancellationToken);
        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        string userKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = SubscriptionReference(userKey, idempotencyKey),
                PaymentCollectionMethod = await ResolveCollectionMethodAsync(cancellationToken)
            }
        };

        var response = await _client.CreateSubscriptionAsync(request, cancellationToken);
        if (response.Subscription is null)
        {
            throw new MaxioApiException("createSubscription", 201, new[] { "The provider did not return a subscription." });
        }

        return MapSubscription(response.Subscription);
    }

    private Task<MaxioSiteInfo> GetSiteAsync(CancellationToken cancellationToken) =>
        _siteCache.GetAsync(_client, TimeSpan.FromSeconds(Math.Max(1, _options.SiteCacheSeconds)), cancellationToken);

    /// <summary>
    /// Decides how the subscription is collected.
    /// <para>
    /// eShopOnWeb captures no card details, so a signup must not depend on a stored payment method:
    /// with <c>automatic</c> collection Maxio rejects the signup outright ("no payment method was on
    /// file for the balance"). The invoicing collection methods are the ones that work, and which of
    /// them is valid depends on the site's architecture - <c>remittance</c> under Relationship
    /// Invoicing, <c>invoice</c> on legacy Statements sites. An operator whose deployment does capture
    /// payment methods can override this with Maxio:PaymentCollectionMethod.
    /// </para>
    /// </summary>
    private async Task<string> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod))
        {
            return _options.PaymentCollectionMethod!.Trim().ToLowerInvariant();
        }

        var site = await GetSiteAsync(cancellationToken);
        if (site.RelationshipInvoicingEnabled is null)
        {
            _logger.LogWarning(
                "The Maxio site architecture is unknown; defaulting new subscriptions to remittance collection. " +
                "Set Maxio:PaymentCollectionMethod to pin this.");
        }

        return site.RelationshipInvoicingEnabled == false ? "invoice" : "remittance";
    }

    /// <summary>
    /// The value to put in the <c>product_family_id</c> path segment. The spec allows the family's id
    /// or its handle prefixed with <c>handle:</c>; handles are stable across catalog re-seeds, so we
    /// always address the family by handle.
    /// </summary>
    private string ProductFamilyPathValue() => $"handle:{_options.ProductFamilyHandle}";

    /// <summary>
    /// The customer reference this application stores on Maxio. Derived from the user's stable key
    /// rather than a database id, so the mapping survives an identity store that re-seeds on restart.
    /// </summary>
    private string CustomerReference(string userKey) => $"{_options.ReferencePrefix}:{userKey}";

    /// <summary>
    /// The subscription reference this application stores on Maxio. Deterministic in the user and the
    /// idempotency key, which is what lets a repeated signup be recognised as a replay.
    /// </summary>
    private string SubscriptionReference(string userKey, string idempotencyKey) =>
        $"{_options.ReferencePrefix}:sub:{userKey}:{idempotencyKey}";

    private void EnsureConfigured()
    {
        var failures = _options.Validate();
        if (failures.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. " + string.Join(" ", failures));
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        PricePointId = product.ProductPricePointId ?? product.DefaultProductPricePointId,
        PricePointName = product.ProductPricePointName,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ArchivedAt = product.ArchivedAt
    };

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName,
        Organization = customer.Organization
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanId = subscription.Product?.Id,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialStartedAt = subscription.TrialStartedAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        TotalRevenueInCents = subscription.TotalRevenueInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Customer = subscription.Customer is null ? null : MapCustomer(subscription.Customer)
    };
}
