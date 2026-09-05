using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API. Maxio is the system of record for customers
/// and subscriptions: nothing is cached or persisted locally, every call reflects live state.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans");

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                IntervalCount = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));

        var customerId = await EnsureCustomerAsync(customerReference, email, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(customerReference, planHandle);

        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return ToCustomerSubscription(existing, isNewlyCreated: false);
        }

        var createRequest = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subscriptionReference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", createRequest, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Likely a race with a concurrent duplicate request: re-check before giving up.
            var raced = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return ToCustomerSubscription(raced, isNewlyCreated: false);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, $"Maxio rejected the subscription request: {body}");
        }

        await EnsureSuccessAsync(response, "create subscription");

        var created = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (created?.Subscription is null)
            throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription response.");

        return ToCustomerSubscription(created.Subscription, isNewlyCreated: true);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdByReferenceAsync(customerReference, cancellationToken);
        if (customerId is null)
            return Array.Empty<CustomerSubscription>();

        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions");

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => ToCustomerSubscription(s!, isNewlyCreated: false))
            .ToList();
    }

    private async Task<long> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken)
    {
        var customerId = await FindCustomerIdByReferenceAsync(customerReference, cancellationToken);
        if (customerId is not null)
            return customerId.Value;

        var (firstName, lastName) = SplitDisplayName(email);

        var createRequest = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", createRequest, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Likely a race with a concurrent duplicate request: re-check before giving up.
            var raced = await FindCustomerIdByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
                return raced.Value;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, $"Maxio rejected the customer request: {body}");
        }

        await EnsureSuccessAsync(response, "create customer");

        var created = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (created?.Customer is null)
            throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer response.");

        return created.Customer.Id;
    }

    private async Task<long?> FindCustomerIdByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, "look up customer");

        var found = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return found?.Customer?.Id;
    }

    private async Task<SubscriptionWire?> FindSubscriptionByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(subscriptionReference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, "look up subscription");

        var found = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return found?.Subscription;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException(response.StatusCode, $"Failed to {action} via Maxio ({(int)response.StatusCode} {response.StatusCode}): {body}");
    }

    private static CustomerSubscription ToCustomerSubscription(SubscriptionWire wire, bool isNewlyCreated) => new()
    {
        MaxioSubscriptionId = wire.Id,
        PlanHandle = wire.Product?.Handle ?? string.Empty,
        PlanName = wire.Product?.Name ?? string.Empty,
        Price = wire.ProductPriceInCents / 100m,
        State = wire.State,
        NextBillingDate = wire.CurrentPeriodEndsAt,
        CreatedAt = wire.CreatedAt,
        IsNewlyCreated = isNewlyCreated
    };

    private static string BuildSubscriptionReference(string customerReference, string planHandle) =>
        $"eshoponweb:{customerReference}:{planHandle}";

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        // eShopOnWeb's Identity model doesn't capture a first/last name for users, only
        // username/email, so derive a reasonable display name for the Maxio customer record.
        var localPart = email.Split('@')[0];
        var firstName = localPart.Length > 0
            ? char.ToUpperInvariant(localPart[0]) + localPart[1..]
            : "eShopOnWeb";
        return (firstName, "Customer");
    }
}
