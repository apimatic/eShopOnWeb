using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await ThrowIfUnsuccessful(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => ToPlan(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfUnsuccessful(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
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
            Customer = new CreateCustomerWire
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, SerializerOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference must be unique; a 422 here most likely means a concurrent request
            // (e.g. a double-click) already created the customer for this reference.
            var raceWinner = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }
        }

        await ThrowIfUnsuccessful(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty customer payload.");
        }

        return ToCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await ThrowIfUnsuccessful(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => ToSubscription(e.Subscription!))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, SerializerOptions, cancellationToken);
        await ThrowIfUnsuccessful(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription payload.");
        }

        return ToSubscription(envelope.Subscription);
    }

    private static MaxioPlan ToPlan(ProductWire product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        IntervalCount = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static MaxioCustomer ToCustomer(CustomerWire customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference ?? string.Empty,
        Email = customer.Email
    };

    private static MaxioSubscription ToSubscription(SubscriptionWire subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        NextBillingAt = subscription.CurrentPeriodEndsAt
    };

    private static async Task ThrowIfUnsuccessful(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ExtractErrorMessage(body, response.StatusCode));
    }

    private static string ExtractErrorMessage(string body, HttpStatusCode statusCode)
    {
        try
        {
            var errors = JsonSerializer.Deserialize<ErrorsEnvelope>(body, SerializerOptions)?.Errors ?? default;

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray().Select(e => e.ToString());
                return string.Join("; ", messages);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}");
                return string.Join("; ", messages);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw-body fallback below.
        }

        return string.IsNullOrWhiteSpace(body) ? $"Maxio request failed with status {(int)statusCode}." : body;
    }
}
