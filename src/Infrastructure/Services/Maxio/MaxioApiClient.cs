using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioApiClient
{
    private const string JsonMediaType = "application/json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioProductFamilyEnvelope>>("product_families.json", cancellationToken);
        return envelopes.Select(e => e.ProductFamily).ToList();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        const int perPage = 200;
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
                $"product_families/{productFamilyId}/products.json?per_page={perPage}&page={page}", cancellationToken);
            products.AddRange(envelopes.Select(e => e.Product));

            if (envelopes.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
        return envelope.Site;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response, content);
        }

        return Deserialize<MaxioCustomerEnvelope>(content).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest { Customer = customer };
        var envelope = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerEnvelope>("customers.json", payload, cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest { Subscription = subscription };
        var envelope = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionEnvelope>("subscriptions.json", payload, cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response, content);
        }

        return Deserialize<T>(content);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, mediaType: new System.Net.Http.Headers.MediaTypeHeaderValue(JsonMediaType))
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response, content);
        }

        return Deserialize<TResponse>(content);
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? throw new MaxioApiException(500, "Maxio returned an empty response body.");
    }

    private static MaxioApiException BuildException(HttpResponseMessage response, string content)
    {
        var statusCode = (int)response.StatusCode;
        var errors = ParseErrors(content);
        var message = errors.Count > 0
            ? $"Maxio API request failed with status {statusCode}: {string.Join(" ", errors)}"
            : $"Maxio API request failed with status {statusCode}. Response: {content}";

        return new MaxioApiException(statusCode, message, errors);
    }

    private static List<string> ParseErrors(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                var errors = new List<string>();
                if (errorsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var error in errorsElement.EnumerateArray())
                    {
                        errors.Add(error.ValueKind == JsonValueKind.String ? error.GetString()! : error.GetRawText());
                    }
                }
                else if (errorsElement.ValueKind == JsonValueKind.String)
                {
                    errors.Add(errorsElement.GetString()!);
                }

                return errors;
            }
        }
        catch (JsonException)
        {
        }

        return new List<string>();
    }
}
