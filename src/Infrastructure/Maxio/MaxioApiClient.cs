using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Hand-written client for the Maxio Advanced Billing API, built strictly against the
/// operations, parameters and schemas described in maxio-spec/openapi.yaml. The HttpClient
/// passed in is expected to already have its BaseAddress and Basic-Auth header configured
/// (see PublicApi's Program.cs).
/// </summary>
public class MaxioApiClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public MaxioApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(string productFamilyHandle)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await _http.GetAsync(path);
        await EnsureSuccessAsync(response);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions) ?? new();
        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => MapProduct(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _http.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer)
    {
        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateWireCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference,
            },
        };

        using var response = await _http.PostAsJsonAsync("customers.json", payload, JsonOptions);
        await EnsureSuccessAsync(response);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio did not return a customer body.", response.StatusCode);
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await _http.GetAsync(path);
        await EnsureSuccessAsync(response);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions) ?? new();
        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!, customerId))
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate request)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateWireSubscription
            {
                ProductHandle = request.ProductHandle,
                CustomerReference = request.CustomerReference,
                PaymentCollectionMethod = request.PaymentCollectionMethod,
            },
        };

        using var response = await _http.PostAsJsonAsync("subscriptions.json", payload, JsonOptions);
        await EnsureSuccessAsync(response);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio did not return a subscription body.", response.StatusCode);
        }

        return MapSubscription(envelope.Subscription, envelope.Subscription.Customer?.Id ?? 0);
    }

    private static MaxioProduct MapProduct(WireProduct p) => new()
    {
        Id = p.Id,
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        IntervalCount = p.Interval,
        IntervalUnit = p.IntervalUnit ?? "month",
        RequireCreditCard = p.RequireCreditCard,
        Taxable = p.Taxable,
        HasTrial = p.TrialInterval is > 0,
    };

    private static MaxioCustomer MapCustomer(WireCustomer c) => new()
    {
        Id = c.Id,
        Reference = c.Reference,
        Email = c.Email,
        FirstName = c.FirstName,
        LastName = c.LastName,
    };

    private static MaxioSubscription MapSubscription(WireSubscription s, long fallbackCustomerId) => new()
    {
        Id = s.Id,
        CustomerId = s.Customer?.Id ?? fallbackCustomerId,
        State = s.State,
        ProductHandle = s.Product?.Handle,
        ProductName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents,
        NextAssessmentAt = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CreatedAt = s.CreatedAt,
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        throw new MaxioApiException(BuildErrorMessage(response.StatusCode, body), response.StatusCode, body);
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray().Select(e => e.ToString()),
                    JsonValueKind.Object => errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"),
                    JsonValueKind.String => new[] { errors.GetString() ?? string.Empty },
                    _ => new[] { errors.ToString() },
                };
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    return $"Maxio API returned {(int)statusCode}: {joined}";
                }
            }
        }
        catch (JsonException)
        {
            // Body wasn't the expected error shape - fall through to the raw-body message below.
        }

        return $"Maxio API returned {(int)statusCode}: {body}";
    }
}
