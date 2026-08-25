using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that represent an open (non-terminated) subscription in Advanced Billing.
    private static readonly HashSet<string> OpenStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "awaiting_signup", "past_due", "unpaid", "on_hold"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioClient maxioClient, IOptions<MaxioSettings> settings, ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt is null).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

        var existing = await FindOpenSubscriptionAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Customer {CustomerId} already has an open subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                customer.Id, existing.Id, planHandle);
            return new SubscribeResult(existing, Created: false);
        }

        try
        {
            var created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = customerReference,
                // Remittance billing issues an invoice instead of capturing a card payment,
                // so signup works for products that don't require a payment method.
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);
            return new SubscribeResult(created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the subscription; re-check before surfacing the error.
            var raced = await FindOpenSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (raced is not null)
            {
                return new SubscribeResult(raced, Created: false);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness violation from a concurrent create: the customer now exists.
            var raced = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindOpenSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && OpenStates.Contains(s.State));
    }
}
