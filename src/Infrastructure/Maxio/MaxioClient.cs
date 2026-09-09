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
/// HTTP implementation of <see cref="IMaxioClient"/> against Maxio Advanced Billing.
/// Authentication is HTTP Basic with the site API key as the username and a fixed
/// placeholder ("x") as the password, per the Maxio Advanced Billing API.
/// </summary>
public class MaxioClient : IMaxioClient
{
    public const string HttpClientName = "Maxio";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(IHttpClientFactory httpClientFactory, IOptions<MaxioOptions> options)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: Maxio:ApiKey is required (from MAXIO_API_KEY).");
        }

        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetAsync<List<MaxioProductEnvelope>>("products.json", cancellationToken) ?? new List<MaxioProductEnvelope>();
        return products.Select(p => p.Product).Where(p => p is not null).Select(p => p!).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers.json?reference={Uri.EscapeDataString(reference)}";
        var (status, content) = await SendAsync(HttpMethod.Get, url, cancellationToken);
        if (status == HttpStatusCode.NotFound)
        {
            return null;
        }
        EnsureSuccess(status, content);
        var customers = Deserialize<List<MaxioCustomerEnvelope>>(content) ?? new List<MaxioCustomerEnvelope>();
        return customers.Select(c => c.Customer).FirstOrDefault(c => c is not null);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var (_, content) = await SendJsonAsync(HttpMethod.Post, "customers.json",
            new MaxioCreateCustomerEnvelope { Customer = request }, cancellationToken);
        var customer = Deserialize<MaxioCustomerEnvelope>(content)?.Customer;
        if (customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.InternalServerError,
                Array.Empty<string>(), "Maxio returned an unexpected response when creating a customer.");
        }
        return customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionEnvelope
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                CustomerId = customerId,
                ProductId = productId,
                // Verified live: without this the sandbox rejects signup with
                // "No payment method was on file" (plans do not require a card, and
                // the invoicing collection method is how Maxio supports cardless signup).
                PaymentCollectionMethod = "invoice"
            }
        };
        var (_, content) = await SendJsonAsync(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        var subscription = Deserialize<MaxioSubscriptionEnvelope>(content)?.Subscription;
        if (subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.InternalServerError,
                Array.Empty<string>(), "Maxio returned an unexpected response when creating a subscription.");
        }
        return subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var url = $"subscriptions.json?customer_id={customerId}";
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>(url, cancellationToken) ?? new List<MaxioSubscriptionEnvelope>();
        return subscriptions.Select(s => s.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        var (status, content) = await SendAsync(HttpMethod.Get, url, ct);
        EnsureSuccess(status, content);
        return Deserialize<T>(content);
    }

    private async Task<(HttpStatusCode Status, string Content)> SendJsonAsync(HttpMethod method, string url, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload, options: SerializerOptions) };
        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, content);
        }
        return (response.StatusCode, content);
    }

    private async Task<(HttpStatusCode Status, string Content)> SendAsync(HttpMethod method, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, content);
    }

    private static void EnsureSuccess(HttpStatusCode status, string content)
    {
        if (status < HttpStatusCode.OK || status >= HttpStatusCode.MultipleChoices)
        {
            throw BuildException(status, content);
        }
    }

    private static MaxioApiException BuildException(HttpStatusCode status, string content)
        => new(status, ParseErrors(content));

    private static IReadOnlyList<string> ParseErrors(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => errors.EnumerateArray()
                        .Select(e => e.GetString()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList(),
                    JsonValueKind.String => new List<string> { errors.GetString()! },
                    _ => new List<string>()
                };
            }
        }
        catch (JsonException)
        {
            // fall through to generic handling
        }
        return new List<string>();
    }

    private static T? Deserialize<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        return JsonSerializer.Deserialize<T>(content);
    }

    private sealed class MaxioProductEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private sealed class MaxioCustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private sealed class MaxioCreateCustomerEnvelope
    {
        [JsonPropertyName("customer")]
        public MaxioCreateCustomerRequest Customer { get; set; } = new();
    }

    private sealed class MaxioCreateSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
    }

    private sealed class MaxioCreateSubscriptionBody
    {
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("product_id")]
        public int ProductId { get; set; }

        [JsonPropertyName("payment_collection_method")]
        public string PaymentCollectionMethod { get; set; } = "invoice";
    }

    private sealed class MaxioSubscriptionEnvelope
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }
}
