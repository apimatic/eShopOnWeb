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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _baseUrl = options.Value.ResolveBaseUrl();
        var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
        return envelope?.Site ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty site response.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        var page = 1;
        var products = new List<MaxioProduct>();

        while (true)
        {
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
                $"products.json?page={page}&per_page={pageSize}", cancellationToken) ?? new();
            products.AddRange(envelopes.Select(x => x.Product));
            if (envelopes.Count < pageSize)
            {
                return products;
            }

            page++;
        }
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"products/handle/{Uri.EscapeDataString(handle)}.json", null, cancellationToken, allowNotFound: true);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            return (await ReadAsync<MaxioProductEnvelope>(response, cancellationToken)).Product;
        }
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken, allowNotFound: true);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            return (await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
    {
        var body = new
        {
            customer = new
            {
                first_name = customer.FirstName,
                last_name = customer.LastName,
                email = customer.Email,
                reference = customer.Reference
            }
        };
        using var response = await SendRequiredAsync(HttpMethod.Post, "customers.json", body, cancellationToken);
        return (await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken) ?? new();
        return envelopes.Select(x => x.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = subscription.ProductHandle,
                customer_reference = subscription.CustomerReference,
                reference = subscription.SubscriptionReference,
                payment_collection_method = subscription.PaymentCollectionMethod
            },
            uniqueness_token = subscription.UniquenessToken
        };
        using var response = await SendRequiredAsync(HttpMethod.Post, "subscriptions.json", body, cancellationToken);
        return (await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendRequiredAsync(HttpMethod.Get, path, null, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private Task<HttpResponseMessage> SendRequiredAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken) =>
        SendAsync(method, path, body, cancellationToken, allowNotFound: false)!;

    private async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(null, "Maxio did not respond before the request timed out.", method != HttpMethod.Get);
        }
        catch (HttpRequestException)
        {
            throw new MaxioApiException(null, "Maxio could not be reached.", method != HttpMethod.Get);
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = response.StatusCode;
        var message = await ReadErrorMessageAsync(response, cancellationToken);
        response.Dispose();
        throw new MaxioApiException(statusCode, message);
    }

    private Uri BuildUri(string path) => new($"{_baseUrl.TrimEnd('/')}/{path}", UriKind.Absolute);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    var messages = errors.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x));
                    var combined = string.Join(" ", messages!);
                    if (!string.IsNullOrWhiteSpace(combined)) return combined;
                }

                if (errors.ValueKind == JsonValueKind.String) return errors.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Return a stable message rather than exposing an upstream HTML/error body.
        }

        return $"Maxio returned HTTP {(int)response.StatusCode}.";
    }
}
