using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Adapter implementing <see cref="IMaxioSubscriptionGateway"/> against the real Maxio Advanced
/// Billing API. Maxio is the system of record: this class holds no local subscription state, it
/// always resolves the buyer's Maxio customer via the stable "reference" it assigned at signup.
/// </summary>
internal class MaxioSubscriptionGateway : IMaxioSubscriptionGateway
{
    // Subscription states in which a buyer is considered already enrolled in a plan.
    // See https://.../api-reference/subscriptions/read-subscription for the full state machine.
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "past_due", "soft_failure", "unpaid"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioSiteCapabilities _siteCapabilities;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionGateway(MaxioApiClient client, MaxioSiteCapabilities siteCapabilities, IOptions<MaxioOptions> options)
    {
        _client = client;
        _siteCapabilities = siteCapabilities;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.GetAsync<List<ProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json",
            cancellationToken);

        return (products ?? new List<ProductEnvelope>())
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(ToSubscriptionPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var customerId = await FindOrCreateCustomerAsync(buyerId, email, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (existing is not null)
        {
            return ToCustomerSubscription(existing);
        }

        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = await _siteCapabilities.GetCardlessPaymentCollectionMethodAsync(cancellationToken)
            },
            // Deterministic per (buyer, plan): a genuine double-click resubmits the same token, so
            // Maxio collapses it into a single subscription instead of creating a duplicate. See
            // https://.../about-the-api/duplicate-prevention.
            UniquenessToken = BuildUniquenessToken(buyerId, planHandle)
        };

        var created = await _client.PostIdempotentAsync<CreateSubscriptionRequest, SubscriptionEnvelope>(
            "subscriptions.json", createRequest, cancellationToken);

        if (created?.Subscription is not null)
        {
            return ToCustomerSubscription(created.Subscription);
        }

        // Maxio returned 409: a request with this uniqueness_token was already accepted (e.g. the
        // original click of a double-click). Re-fetch to return the subscription it produced.
        var resolved = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.Conflict,
                $"Maxio rejected the subscribe request as a duplicate, but no matching subscription for plan '{planHandle}' could be found for customer {customerId}.");

        return ToCustomerSubscription(resolved);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var customer = await LookupCustomerAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToCustomerSubscription).ToList();
    }

    private async Task<long> FindOrCreateCustomerAsync(string buyerId, string email, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = localPart,
                LastName = "eShopOnWeb Customer",
                Email = email,
                Reference = buyerId
            }
        };

        try
        {
            var created = await _client.PostAsync<CreateCustomerRequest, CustomerEnvelope>(
                "customers.json", createRequest, cancellationToken);
            return created.Customer.Id;
        }
        catch (MaxioApiException)
        {
            // A concurrent request (e.g. the other half of a double-click) may have created the
            // customer for this reference between our lookup and our create. Maxio enforces
            // reference uniqueness, so recover by looking it up again rather than failing.
            var recovered = await LookupCustomerAsync(buyerId, cancellationToken);
            if (recovered is not null)
            {
                return recovered.Id;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var envelope = await _client.GetAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(buyerId)}", cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await _client.GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return (envelopes ?? new List<SubscriptionEnvelope>()).Select(e => e.Subscription).ToList();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            LiveSubscriptionStates.Contains(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    // Maxio remembers a uniqueness_token for 60 minutes and 409s *any* resubmission of it,
    // including a retry of a request that originally failed validation - not just successes. A
    // token scoped only to (buyer, plan) would therefore lock a buyer out of retrying a genuinely
    // fixable failure (e.g. a transient error) for up to an hour. Bucketing by a short time window
    // keeps real double-clicks (typically <1s apart) collapsed into one token, while letting a
    // later, distinct attempt get a fresh one.
    private static readonly TimeSpan UniquenessTokenBucketSize = TimeSpan.FromSeconds(10);

    private static string BuildUniquenessToken(string buyerId, string planHandle)
    {
        var bucket = DateTimeOffset.UtcNow.Ticks / UniquenessTokenBucketSize.Ticks;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb-subscribe:{buyerId}:{planHandle}:{bucket}"));
        return Convert.ToHexString(hash)[..32];
    }

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        PriceAmount = product.PriceInCents / 100m,
        BillingIntervalCount = product.Interval,
        BillingIntervalUnit = product.IntervalUnit
    };

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceAmount = (subscription.Product?.PriceInCents ?? 0) / 100m,
        State = subscription.State,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };
}
