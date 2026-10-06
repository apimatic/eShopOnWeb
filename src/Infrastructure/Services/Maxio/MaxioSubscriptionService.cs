using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
///
/// Idempotency: the eShopOnWeb user id is encoded into the Maxio customer
/// <c>reference</c> and the (user, plan) pair is encoded into the subscription
/// <c>reference</c>. Maxio enforces uniqueness on both, so a double-click can
/// never create two customers or two subscriptions for the same plan.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioApiClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.ProductFamilyHandle
            ?? throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");

        var families = await _client.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f => string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));
        if (family is null)
        {
            throw new MaxioConfigurationException($"Maxio product family '{familyHandle}' was not found on the configured site.");
        }

        var products = await _client.ListProductsAsync(cancellationToken);
        return products
            .Where(p => p.ProductFamily?.Id == family.Id && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan(
                p.Id,
                p.Handle ?? string.Empty,
                p.Name ?? string.Empty,
                p.Description,
                p.PriceInCents,
                p.Interval,
                p.IntervalUnit ?? "month",
                family.Handle ?? familyHandle))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<Subscription> SubscribeAsync(SubscriptionUser user, string planHandle, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reference = BuildSubscriptionReference(user.Id, planHandle);

        var existing = await _client.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription {SubscriptionId} already exists for user {UserId} on plan {PlanHandle}.", existing.Id, user.Id, planHandle);
            return Map(existing);
        }

        try
        {
            var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", created.Id, user.Id, planHandle);
            return Map(created);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request created the subscription with the same reference.
            var raced = await _client.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Subscription {SubscriptionId} was created concurrently for user {UserId} on plan {PlanHandle}.", raced.Id, user.Id, planHandle);
                return Map(raced);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await _client.LookupCustomerByReferenceAsync(BuildCustomerReference(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var existing = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = reference
            }, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, user.Id);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request created the customer with the same reference.
            var raced = await _client.LookupCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private static string BuildCustomerReference(string userId) => $"eshop-{userId}";

    private static string BuildSubscriptionReference(string userId, string planHandle) => $"eshop-{userId}-{planHandle}";

    private static Subscription Map(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.State ?? "unknown",
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents,
        subscription.BalanceInCents,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt,
        subscription.ActivatedAt,
        subscription.CreatedAt,
        subscription.Reference);
}
