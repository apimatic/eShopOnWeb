using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// <see cref="IMaxioService"/> implementation backed by the Maxio Advanced Billing REST API.
/// Registered as a typed HttpClient - see PublicApi's Program.cs for base address/auth setup.
/// </summary>
public class MaxioService : IMaxioService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;

    public MaxioService(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _productFamilyHandle = settings.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var handle = Uri.EscapeDataString(_productFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/handle:{handle}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var products = await ReadArrayAsync<ProductWrapperDto>(response, cancellationToken);
        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan(p!.Id, p.Handle ?? string.Empty, p.Name, p.PriceInCents, p.Interval, p.IntervalUnit))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapperDto>(JsonOptions, cancellationToken);
        return wrapper?.Customer is null ? null : MapCustomer(wrapper.Customer);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateCustomerRequestDto
        {
            Customer = new CreateCustomerAttributesDto
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference must be unique - a concurrent request (e.g. a double-click) may have
            // created the customer between our lookup and this create. Re-check before failing.
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapperDto>(JsonOptions, cancellationToken);
        if (wrapper?.Customer is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio create customer response did not contain a customer.");
        }

        return MapCustomer(wrapper.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<MaxioSubscription>();
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var subscriptions = await ReadArrayAsync<SubscriptionWrapperDto>(response, cancellationToken);
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionRequestDto
        {
            Subscription = new CreateSubscriptionAttributesDto
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionWrapperDto>(JsonOptions, cancellationToken);
        if (wrapper?.Subscription is null)
        {
            throw new MaxioApiException((int)response.StatusCode, "Maxio create subscription response did not contain a subscription.");
        }

        return MapSubscription(wrapper.Subscription);
    }

    private static MaxioCustomer MapCustomer(CustomerDto customer) => new(customer.Id, customer.Reference ?? string.Empty, customer.Email);

    private static MaxioSubscription MapSubscription(SubscriptionDto subscription) => new(
        subscription.Id,
        subscription.State,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.Product?.PriceInCents,
        subscription.Product?.IntervalUnit,
        subscription.Product?.Interval,
        subscription.CurrentPeriodEndsAt,
        subscription.NextAssessmentAt);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, $"Maxio API request to {response.RequestMessage?.RequestUri} failed with status {(int)response.StatusCode}: {body}");
    }

    /// <summary>
    /// Maxio list endpoints are documented inconsistently (some as a bare JSON array, some
    /// wrapped in a named object property alongside pagination metadata). This reads either
    /// shape so a documentation-vs-reality mismatch doesn't break list parsing.
    /// </summary>
    private static async Task<List<TWrapper>> ReadArrayAsync<TWrapper>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(e => e.Deserialize<TWrapper>(JsonOptions)!).ToList();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray().Select(e => e.Deserialize<TWrapper>(JsonOptions)!).ToList();
                }
            }
        }

        return new List<TWrapper>();
    }
}
