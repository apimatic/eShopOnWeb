using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public interface IMaxioApiClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerWrite customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionWrite subscription, CancellationToken cancellationToken);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        string family = long.TryParse(productFamilyHandle, out _)
            ? productFamilyHandle
            : $"handle:{productFamilyHandle}";

        var response = await SendAsync(HttpMethod.Get, $"/product_families/{Uri.EscapeDataString(family)}/products.json?per_page=200", null, cancellationToken);
        var envelopes = await DeserializeAsync<IReadOnlyList<MaxioProductEnvelope>>(response, cancellationToken);
        return envelopes.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken, notFoundIsNull: true);
        if (response is null)
        {
            return null;
        }

        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerWrite customer, CancellationToken cancellationToken)
    {
        var payload = new { customer };
        var response = await SendAsync(HttpMethod.Post, "/customers.json", payload, cancellationToken);
        var envelope = await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(System.Net.HttpStatusCode.OK, "Maxio returned an empty customer payload.", Array.Empty<string>());
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json", null, cancellationToken);
        var envelopes = await DeserializeAsync<IReadOnlyList<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionWrite subscription, CancellationToken cancellationToken)
    {
        var payload = new { subscription };
        var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", payload, cancellationToken);
        var envelope = await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(System.Net.HttpStatusCode.Created, "Maxio returned an empty subscription payload.", Array.Empty<string>());
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        object? payload,
        CancellationToken cancellationToken,
        bool notFoundIsNull = false)
    {
        string baseUrl = _options.ResolveBaseUrl();
        using var request = new HttpRequestMessage(method, new Uri(baseUrl + pathAndQuery));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Authorization", BasicAuthorizationHeader());

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: MaxioJson.Default);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (notFoundIsNull && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        _logger.LogWarning("Maxio API call {Method} {Path} failed with status {Status}: {Body}",
            method, pathAndQuery, (int)response.StatusCode, body);

        throw new MaxioApiException(response.StatusCode, ExtractMessage(body, pathAndQuery), ExtractErrors(body));
    }

    private string BasicAuthorizationHeader()
    {
        return "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
    }

    private static string ExtractMessage(string body, string pathAndQuery)
    {
        var errors = ExtractErrors(body);
        if (errors.Count > 0)
        {
            return $"Maxio request to {pathAndQuery} failed: {string.Join(" ", errors)}";
        }

        return $"Maxio request to {pathAndQuery} failed with an unexpected response.";
    }

    private static IReadOnlyList<string> ExtractErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            if (document.RootElement.TryGetProperty("errors", out var errorsElement))
            {
                if (errorsElement.ValueKind == JsonValueKind.Array)
                {
                    return errorsElement
                        .EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }

                if (errorsElement.ValueKind == JsonValueKind.Object)
                {
                    return errorsElement.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value}")
                        .ToList();
                }
            }
        }
        catch (JsonException)
        {
        }

        return new List<string> { body };
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            return await response.Content.ReadFromJsonAsync<T>(MaxioJson.Default, cancellationToken) ?? throw new InvalidOperationException("Empty Maxio response.");
        }
    }
}
