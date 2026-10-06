using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const string ReferencePrefix = "eshoponweb:";

    // Per Billing API docs: these are the non-end-of-life states. A subscription in any
    // of them means the customer is already signed up for the plan; end-of-life states
    // (canceled, expired, failed_to_create, ...) allow a fresh signup.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "awaiting_signup", "trialing", "assessing", "active",
        "soft_failure", "past_due", "unpaid", "suspended", "on_hold"
    };

    private readonly IMaxioBillingClient _billingClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioBillingClient billingClient, IOptions<MaxioOptions> options)
    {
        _billingClient = billingClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
        => await _billingClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

    public async Task<MaxioProduct?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var product = await _billingClient.GetProductByHandleAsync(planHandle, cancellationToken);
        if (product is null || !string.IsNullOrEmpty(product.ArchivedAt)) return null;
        if (!string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            // Exists in Maxio but outside the catalog this application exposes.
            return null;
        }
        return product;
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string userName, string email, CancellationToken cancellationToken = default)
    {
        var reference = BuildCustomerReference(userName);
        var existing = await _billingClient.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        var request = new MaxioCustomerCreateRequest
        {
            Reference = reference,
            FirstName = "eShopOnWeb",
            LastName = "Customer",
            Email = email,
            Organization = "eShopOnWeb"
        };

        try
        {
            return await _billingClient.CreateCustomerAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference may have been claimed concurrently (e.g. a double-click that
            // raced past the lookup). Re-read; only fail if it genuinely does not exist.
            var raced = await _billingClient.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null) return raced;
            throw;
        }
    }

    public async Task<(MaxioSubscription Subscription, bool Created)> SubscribeAsync(MaxioCustomer customer, MaxioProduct plan, CancellationToken cancellationToken = default)
    {
        // Idempotency pre-check: an existing live subscription for the same plan wins.
        var existing = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var current = FindLiveSubscription(existing, plan);
        if (current is not null) return (current, Created: false);

        try
        {
            // Payment collection method depends on the site architecture (per Billing API
            // docs): RI sites accept remittance/automatic/prepaid, legacy sites
            // invoice/automatic. Remittance/invoice both sign up without card capture.
            var collectionMethod = await _billingClient.IsRelationshipInvoicingEnabledAsync(cancellationToken)
                ? "remittance"
                : "invoice";
            var created = await _billingClient.CreateSubscriptionAsync(plan.Handle!, customer.Id, collectionMethod, Guid.NewGuid().ToString(), cancellationToken);
            return (created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // DuplicatePrevention::DuplicateSubmissionError — an identical request was
            // received concurrently. Resolve to the subscription that was created.
            var afterRace = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var raced = FindLiveSubscription(afterRace, plan);
            if (raced is not null) return (raced, Created: false);
            throw;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByUserAsync(string userName, CancellationToken cancellationToken = default)
        => await _billingClient.GetCustomerByReferenceAsync(BuildCustomerReference(userName), cancellationToken);

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(MaxioCustomer customer, CancellationToken cancellationToken = default)
        => await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

    private static MaxioSubscription? FindLiveSubscription(IEnumerable<MaxioSubscription> subscriptions, MaxioProduct plan)
        => subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));

    private static string BuildCustomerReference(string userName)
        => $"{ReferencePrefix}{userName}".ToLowerInvariant();
}