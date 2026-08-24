using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing, which is the
/// system of record: the eShopOnWeb user id is stored as the Maxio customer reference, so no
/// local persistence of the user-to-customer/subscription mapping is required.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which the subscription relationship is over and a re-subscribe is allowed.
    private static readonly HashSet<string> TerminatedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle!, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan(p.Handle!, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(customerReference, email, cancellationToken);

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminatedStates.Contains(s.State));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} already has subscription {SubscriptionId} for plan {ProductHandle}; returning it instead of creating a duplicate.",
                customerReference, existing.Id, productHandle);
            return new SubscribeResult(Map(existing), AlreadyExisted: true);
        }

        var created = await _client.CreateSubscriptionAsync(
            productHandle, customer.Id, $"{customerReference}:{productHandle}", _settings.PaymentCollectionMethod, cancellationToken);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerReference} on plan {ProductHandle}.",
            created.Id, customerReference, productHandle);

        return new SubscribeResult(Map(created), AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        // eShopOnWeb identities carry only an email; Maxio requires a non-blank last name.
        var localPart = email.Split('@')[0];
        try
        {
            return await _client.CreateCustomerAsync(localPart, "User", email, customerReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent request that created the customer first; look it up.
            customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            throw;
        }
    }

    private static SubscriptionDetails Map(MaxioSubscription subscription) =>
        new(
            subscription.Id,
            subscription.State,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.Product?.PriceInCents,
            subscription.Product?.Interval,
            subscription.Product?.IntervalUnit,
            subscription.ActivatedAt,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.CreatedAt);
}
