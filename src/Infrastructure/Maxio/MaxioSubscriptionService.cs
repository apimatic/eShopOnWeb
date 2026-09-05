using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the eShopOnWeb subscription-billing capability against Maxio Advanced Billing,
/// using only the endpoints published in maxio-spec/openapi.yaml:
///   GET  /product_families/handle:{handle}/products.json
///   GET  /customers/lookup.json, POST /customers.json
///   GET  /subscriptions/lookup.json, POST /subscriptions.json
///   GET  /customers/{customer_id}/subscriptions.json
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioApiClient _client;
    private readonly IOptions<MaxioOptions> _options;

    public MaxioSubscriptionService(MaxioApiClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        var products = await _client.GetAsync<List<ProductEnvelope>>(
            $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json", cancellationToken) ?? new List<ProductEnvelope>();

        return products
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Select(product => new SubscriptionPlan
            {
                Handle = product!.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        var subscriptionReference = BuildSubscriptionReference(customerReference, planHandle);

        // Idempotency short-circuit: a repeated/double-click request for the same user+plan
        // finds the subscription created by the first request and returns it unchanged.
        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return ToCustomerSubscription(existing);
        }

        var customerId = await FindOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

        var createRequest = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                PaymentCollectionMethod = "invoice"
            }
        };

        try
        {
            var created = await _client.PostAsync<CreateSubscriptionEnvelope, SubscriptionEnvelope>("subscriptions.json", createRequest, cancellationToken);
            return ToCustomerSubscription(created.Subscription!);
        }
        catch (MaxioApiException)
        {
            // Two concurrent requests could both pass the check above; if Maxio rejected this one
            // because the reference was just taken by the other, return that subscription instead of failing.
            var afterRace = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (afterRace is not null)
            {
                return ToCustomerSubscription(afterRace);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customer.Id}/subscriptions.json", cancellationToken) ?? new List<SubscriptionEnvelope>();

        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToCustomerSubscription(subscription!))
            .ToList();
    }

    private async Task<int> FindOrCreateCustomerAsync(string customerReference, string customerEmail, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var (firstName, lastName) = SplitDisplayName(customerEmail);
        var createRequest = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customerEmail,
                Reference = customerReference
            }
        };

        try
        {
            var created = await _client.PostAsync<CreateCustomerEnvelope, CustomerEnvelope>("customers.json", createRequest, cancellationToken);
            return created.Customer!.Id;
        }
        catch (MaxioApiException)
        {
            // Concurrent double-click race: the other request may have just created this customer.
            var afterRace = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace.Id;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        (await _client.GetAsync<CustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken))?.Customer;

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        (await _client.GetAsync<SubscriptionEnvelope>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken))?.Subscription;

    private static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        $"eshop:{customerReference}:{planHandle}";

    private static (string FirstName, string LastName) SplitDisplayName(string emailOrUsername)
    {
        var atIndex = emailOrUsername.IndexOf('@');
        var localPart = atIndex > 0 ? emailOrUsername[..atIndex] : emailOrUsername;
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Length > 1 ? Capitalize(string.Join(' ', parts.Skip(1))) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
