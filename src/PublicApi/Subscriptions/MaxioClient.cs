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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private const int MaxPageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = _options.GetBaseAddress();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);

        for (var page = 1; ; page++)
        {
            var responses = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get,
                $"product_families/handle:{family}/products.json?page={page}&per_page={MaxPageSize}&include_archived=false",
                null,
                HttpStatusCode.OK,
                cancellationToken);
            var pageProducts = responses
                .Select(response => response.Product)
                .Where(product => product is not null)
                .Cast<MaxioProduct>()
                .ToList();
            products.AddRange(pageProducts);

            if (responses.Count < MaxPageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendOptionalAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendOptionalAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        long? customerId,
        MaxioCreateCustomer? customerAttributes,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                CustomerId = customerId,
                CustomerAttributes = customerAttributes,
                ProductHandle = productHandle,
                Reference = reference,
                PaymentCollectionMethod = "remittance"
            }
        };
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            request,
            HttpStatusCode.Created,
            cancellationToken);
        return response.Subscription ?? throw InvalidResponse("subscription");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            HttpStatusCode.OK,
            cancellationToken);
        return responses
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != expectedStatus)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        return result ?? throw InvalidResponse(typeof(T).Name);
    }

    private async Task<T?> SendOptionalAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken) where T : class
    {
        using var request = CreateRequest(method, path, body);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != expectedStatus)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw InvalidResponse(typeof(T).Name);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        var baseUrl = _httpClient.BaseAddress!.AbsoluteUri.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        return request;
    }

    private static async Task<MaxioApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = GetErrorDetail(body);
        return new MaxioApiException(
            response.StatusCode,
            $"Maxio returned HTTP {(int)response.StatusCode} ({response.StatusCode}){(string.IsNullOrEmpty(detail) ? "." : $": {detail}")}");
    }

    private static string GetErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(item => item.ToString())),
                    JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(item => $"{item.Name}: {item.Value}")),
                    _ => errors.ToString()
                };
            }
        }
        catch (JsonException)
        {
            // The spec allows plain-string 404 responses for some operations.
        }

        return body.Length <= 2000 ? body : body[..2000];
    }

    private static MaxioApiException InvalidResponse(string resource) =>
        new(HttpStatusCode.BadGateway, $"Maxio returned an invalid {resource} response.");
}
