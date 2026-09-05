using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// HTTP implementation of <see cref="IMaxioClient"/> against the Maxio Advanced Billing API,
/// built strictly from the maxio-spec/ OpenAPI contract (paths, request/response shapes,
/// Basic-Auth scheme). The <see cref="HttpClient"/> injected here is expected to already carry
/// the site's base address and Authorization header (see Program.cs registration).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(productFamilyHandle, nameof(productFamilyHandle));

        using var response = await _httpClient.GetAsync(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken) ?? new();
        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new MaxioPlan
            {
                Handle = p!.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequiresPaymentMethod = p.RequireCreditCard
            })
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));

        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return ToCustomer(envelope?.Customer);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces a unique customer reference; a concurrent request (e.g. a
            // double-click) may have created the customer between our lookup and this create.
            var afterRace = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return ToCustomer(envelope?.Customer)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio did not return a customer in the create-customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken) ?? new();
        return envelopes
            .Select(e => ToSubscription(e.Subscription))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return ToSubscription(envelope?.Subscription)
            ?? throw new MaxioApiException((int)response.StatusCode, "Maxio did not return a subscription in the create-subscription response.");
    }

    private static MaxioCustomer? ToCustomer(CustomerPayload? payload) =>
        payload is null
            ? null
            : new MaxioCustomer { Id = payload.Id, Reference = payload.Reference ?? string.Empty, Email = payload.Email ?? string.Empty };

    private static MaxioSubscription? ToSubscription(SubscriptionPayload? payload) =>
        payload is null
            ? null
            : new MaxioSubscription
            {
                Id = payload.Id,
                State = payload.State,
                CustomerId = payload.Customer?.Id ?? 0,
                ProductHandle = payload.Product?.Handle ?? string.Empty,
                ProductName = payload.Product?.Name ?? string.Empty,
                ProductPriceInCents = payload.ProductPriceInCents,
                CurrentPeriodEndsAt = payload.CurrentPeriodEndsAt,
                NextAssessmentAt = payload.NextAssessmentAt
            };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, $"Maxio API request failed with status {(int)response.StatusCode}: {body}");
    }
}
