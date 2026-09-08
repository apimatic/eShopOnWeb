using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Hand-written, spec-driven client for Maxio Advanced Billing. Endpoints, verbs, query
/// parameters and payload shapes all come from <c>maxio-spec/openapi.yaml</c>:
///   - Basic auth: username = API key, password = "x" (components securityScheme "BasicAuth").
///   - JSON content type (the spec's primary response format).
/// Auth and base address are configured on the injected <see cref="HttpClient"/>.
/// </summary>
public sealed class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsAsync(CancellationToken ct = default)
    {
        // GET /products.json -> array of { "product": { ... } }
        var envelope = await GetAsync<ProductEnvelope[]>("products.json", null, ct);

        return (envelope ?? Array.Empty<ProductEnvelope>())
            .Where(e => e.Product is not null)
            .Select(e => e.Product!)
            .Select(MapProduct)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        // GET /customers/lookup.json?reference=<value> -> { "customer": { ... } } (404 when absent)
        var uri = $"customers/lookup.json?{Escape("reference")}={Escape(reference)}";
        var envelope = await GetAsync<CustomerEnvelope>(uri, HttpStatusCode.NotFound, ct);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput input, CancellationToken ct = default)
    {
        // POST /customers.json -> { "customer": { ... } }
        var body = new CustomerRequest
        {
            Customer = new CustomerRequest.CustomerRequestBody
            {
                FirstName = input.FirstName,
                LastName = input.LastName,
                Email = input.Email,
                Reference = input.Reference
            }
        };

        var envelope = await SendForEnvelopeAsync<CustomerEnvelope>(
            HttpMethod.Post, "customers.json", body, HttpStatusCode.OK, ct);

        return envelope.Customer is null
            ? throw new MaxioApiException(0, null, "Maxio returned an empty customer response.")
            : MapCustomer(envelope.Customer);
    }

    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct = default)
    {
        // GET /subscriptions/lookup.json?reference=<value> -> { "subscription": { ... } } (404 when absent)
        var uri = $"subscriptions/lookup.json?{Escape("reference")}={Escape(reference)}";
        var envelope = await GetAsync<SubscriptionEnvelope>(uri, HttpStatusCode.NotFound, ct);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<Subscription> CreateSubscriptionAsync(MaxioSubscriptionInput input, CancellationToken ct = default)
    {
        // POST /subscriptions.json -> { "subscription": { ... } } (201)
        var body = new SubscriptionRequest
        {
            Subscription = new SubscriptionRequest.SubscriptionRequestBody
            {
                ProductHandle = input.ProductHandle,
                CustomerId = input.CustomerId,
                Reference = input.Reference,
                PaymentCollectionMethod = input.PaymentCollectionMethod
            }
        };

        var envelope = await SendForEnvelopeAsync<SubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", body, HttpStatusCode.Created, ct);

        return envelope.Subscription is null
            ? throw new MaxioApiException(0, null, "Maxio returned an empty subscription response.")
            : MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken ct = default)
    {
        // GET /customers/{customer_id}/subscriptions.json -> array of { "subscription": { ... } }
        var envelope = await GetAsync<SubscriptionEnvelope[]>($"customers/{customerId}/subscriptions.json", null, ct);

        return (envelope ?? Array.Empty<SubscriptionEnvelope>())
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    // ---- HTTP plumbing ---------------------------------------------------------------

    private async Task<T?> GetAsync<T>(string uri, HttpStatusCode? nullWhen, CancellationToken ct)
        where T : class
    {
        using var response = await _http.GetAsync(uri, ct);

        if (nullWhen.HasValue && response.StatusCode == nullWhen.Value)
        {
            return null;
        }

        await ThrowForStatusAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(ReadOptions, ct);
    }

    private async Task<T> SendForEnvelopeAsync<T>(HttpMethod method, string uri, object body, HttpStatusCode success, CancellationToken ct)
        where T : class, new()
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body, options: WriteOptions)
        };

        using var response = await _http.SendAsync(request, ct);

        var text = await response.Content.ReadAsStringAsync(ct);

        // Any 2xx is success. Maxio returns 200 for customer create and 201 for subscription
        // create in practice, so we accept the whole success range rather than a single code.
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException((int)response.StatusCode, text, $"Maxio {method.Method} {uri} failed ({(int)response.StatusCode} {response.StatusCode}).");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new T();
        }

        var result = JsonSerializer.Deserialize<T>(text, ReadOptions);
        return result ?? new T();
    }

    private static async Task ThrowForStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        throw BuildException((int)response.StatusCode, text, $"Maxio request to {response.RequestMessage?.RequestUri?.AbsolutePath} failed ({(int)response.StatusCode} {response.StatusCode}).");
    }

    private static MaxioApiException BuildException(int statusCode, string body, string message)
    {
        var errors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("errors", out var errEl))
            {
                switch (errEl.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var item in errEl.EnumerateArray())
                        {
                            errors.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.ToString());
                        }
                        break;
                    case JsonValueKind.String:
                        errors.Add(errEl.GetString()!);
                        break;
                    case JsonValueKind.Object:
                        foreach (var prop in errEl.EnumerateObject())
                        {
                            var value = prop.Value.ValueKind == JsonValueKind.Array
                                ? string.Join(", ", prop.Value.EnumerateArray().Select(v => v.ToString()))
                                : prop.Value.ToString();
                            errors.Add($"{prop.Name}: {value}");
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Body was not JSON — fall through with the raw body only.
        }

        var detail = errors.Count > 0 ? $" {string.Join(" ", errors)}" : (!string.IsNullOrWhiteSpace(body) ? $" {body}" : string.Empty);
        return new MaxioApiException(statusCode, errors, body, message + detail);
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static string Escape(string value) => Uri.EscapeDataString(value);

    // ---- Mapping to domain model -----------------------------------------------------

    private static SubscriptionPlan MapProduct(ProductDto p) => new()
    {
        Id = p.Id,
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty,
        RequireCreditCard = p.RequireCreditCard,
        Archived = p.ArchivedAt.HasValue
    };

    private static MaxioCustomer MapCustomer(CustomerDto c) => new()
    {
        Id = c.Id,
        Reference = c.Reference,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email
    };

    private static Subscription MapSubscription(SubscriptionDto s) => new()
    {
        Id = s.Id,
        State = s.State ?? string.Empty,
        Reference = s.Reference,
        CustomerId = s.Customer?.Id ?? 0,
        ProductHandle = s.Product?.Handle ?? string.Empty,
        ProductName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.Product?.PriceInCents ?? 0,
        Interval = s.Product?.Interval ?? 0,
        IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt,
        CreatedAt = s.CreatedAt,
        ActivatedAt = s.ActivatedAt,
        CanceledAt = s.CanceledAt
    };
}
