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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Hand-written client for the subset of the Maxio Advanced Billing API (maxio-spec/openapi.yaml)
/// this app needs: find/create a customer, list products, and create/list subscriptions.
/// Auth per maxio-spec `securitySchemes.BasicAuth`: HTTP Basic, username = API key, password = "x".
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return ToCustomer(envelope!.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return ToCustomer(envelope!.Customer);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken);
        return (envelopes ?? new List<ProductEnvelope>())
            .Select(e => ToProduct(e.Product))
            .ToList();
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return (envelopes ?? new List<SubscriptionEnvelope>())
            .Select(e => ToSubscription(e.Subscription, customerId))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                // These plans are configured with no required payment method (see README).
                // "remittance" enrolls the subscription without attempting an automatic card
                // charge at signup, matching maxio-spec's own no-card-on-file example.
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return ToSubscription(envelope!.Subscription, subscription.CustomerId);
    }

    private static MaxioCustomer ToCustomer(CustomerWire wire) =>
        new(wire.Id, wire.Reference, wire.Email, wire.FirstName, wire.LastName);

    private static MaxioProduct ToProduct(ProductWire wire) => new(
        wire.Id,
        wire.Handle ?? string.Empty,
        wire.Name,
        wire.Description,
        wire.PriceInCents,
        wire.Interval,
        wire.IntervalUnit,
        wire.ProductFamily?.Handle ?? string.Empty,
        wire.ArchivedAt);

    private static MaxioSubscription ToSubscription(SubscriptionWire wire, int fallbackCustomerId) => new(
        wire.Id,
        wire.State,
        wire.Customer?.Id ?? fallbackCustomerId,
        wire.Product?.Handle ?? string.Empty,
        wire.Product?.Name ?? string.Empty,
        wire.Product?.PriceInCents ?? 0,
        wire.CreatedAt,
        wire.CurrentPeriodEndsAt,
        wire.NextAssessmentAt);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errors = await ExtractErrorsAsync(response, cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, errors);
    }

    private static async Task<IReadOnlyList<string>> ExtractErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new[] { response.ReasonPhrase ?? response.StatusCode.ToString() };
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                return new[] { raw };
            }

            return errorsElement.ValueKind switch
            {
                // errors: ["msg", ...]
                JsonValueKind.Array => errorsElement.EnumerateArray()
                    .Select(e => e.ToString())
                    .ToList(),
                // errors: { "field": "message" | ["message", ...] }
                JsonValueKind.Object => errorsElement.EnumerateObject()
                    .SelectMany(FlattenErrorProperty)
                    .ToList(),
                _ => new[] { errorsElement.ToString() }
            };
        }
        catch (JsonException)
        {
            return new[] { raw };
        }
    }

    private static IEnumerable<string> FlattenErrorProperty(JsonProperty property)
    {
        if (property.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.Value.EnumerateArray())
            {
                yield return $"{property.Name}: {item}";
            }
        }
        else
        {
            yield return $"{property.Name}: {property.Value}";
        }
    }
}
