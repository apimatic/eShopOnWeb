using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly Uri _baseUri;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
        _baseUri = _options.GetBaseUri();
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return responses.Select(response => response.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendNullableAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            HttpStatusCode.OK,
            cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var response = await SendNullableAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            HttpStatusCode.Created,
            cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return responses.Select(response => response.Subscription).ToList();
    }

    private async Task<T?> SendNullableAsync<T>(
        HttpMethod method,
        string relativeUri,
        object? body,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken) where T : class
    {
        using var response = await SendRequestAsync(method, relativeUri, body, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadResponseAsync<T>(response, successStatus, cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUri,
        object? body,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(method, relativeUri, body, cancellationToken);
        return await ReadResponseAsync<T>(response, successStatus, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string relativeUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativeUri));
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != successStatus)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(
            response.StatusCode,
            new[] { "Maxio returned an empty or invalid response." });
    }

    private Uri BuildUri(string relativeUri)
    {
        var root = _baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? _baseUri.AbsoluteUri
            : $"{_baseUri.AbsoluteUri}/";
        return new Uri(new Uri(root, UriKind.Absolute), relativeUri);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var messages = new List<string>();
                CollectErrors(errors, messages);
                if (messages.Count > 0)
                {
                    return messages;
                }
            }
        }
        catch (JsonException)
        {
            // The status code remains authoritative when an upstream error body is malformed.
        }

        return new[] { "The billing provider rejected the request." };
    }

    private static void CollectErrors(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var message = element.GetString();
                if (!string.IsNullOrWhiteSpace(message)) messages.Add(message);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CollectErrors(item, messages);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) CollectErrors(property.Value, messages);
                break;
        }
    }
}
