using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/> built against
/// maxio-spec/openapi.yaml: basic auth (API key as username, "x" as password),
/// server https://{site}.chargify.com, snake_case JSON payloads and the
/// {"errors": [...]} error model from the spec.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;

        _httpClient = httpClient;

        // Maxio:BaseUrl overrides the derived address verbatim when present
        // (trailing slash normalized so relative request URIs resolve against it).
        var baseAddress = !string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? settings.BaseUrl.TrimEnd('/')
            : $"https://{settings.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseAddress + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var url = $"products.json?page={page}&per_page={MaxPageSize}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var batch = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(SerializerOptions, cancellationToken)
                ?? new List<ProductEnvelope>();

            products.AddRange(batch.Where(e => e.Product is not null).Select(e => e.Product!));

            if (batch.Count < MaxPageSize)
            {
                return products;
            }

            page++;
        }
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerBody
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(SerializerOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty customer response." });
        return envelope.Customer ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned a customer response without a customer." });
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(SerializerOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned an empty subscription response." });
        return envelope.Subscription ?? throw new MaxioApiException((int)response.StatusCode, new[] { "Maxio returned a subscription response without a subscription." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var batch = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(SerializerOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();
        return batch.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException((int)response.StatusCode, ParseErrors(body), body);
    }

    /// <summary>
    /// Parses Maxio's error model from the spec: {"errors": ["...", ...]} for most
    /// endpoints, {"errors": {"field": "message"}} for some validation failures.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return errors.EnumerateArray()
                        .Select(e => e.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }

                if (errors.ValueKind == JsonValueKind.Object)
                {
                    return errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value}")
                        .ToList();
                }

                return new[] { errors.ToString() };
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return new[] { error.ToString() };
            }
        }
        catch (JsonException)
        {
            // fall through to raw body
        }

        return new[] { body };
    }

    private sealed class ProductEnvelope
    {
        public MaxioProduct? Product { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class SubscriptionEnvelope
    {
        public MaxioSubscription? Subscription { get; set; }
    }

    private sealed class CreateCustomerEnvelope
    {
        public CreateCustomerBody Customer { get; set; } = new();
    }

    private sealed class CreateCustomerBody
    {
        public string Reference { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class CreateSubscriptionEnvelope
    {
        public CreateSubscriptionBody Subscription { get; set; } = new();
    }

    private sealed class CreateSubscriptionBody
    {
        public string ProductHandle { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string PaymentCollectionMethod { get; set; } = "remittance";
    }
}
