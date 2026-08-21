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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"/product_families/{family}/products.json?per_page=200",
            null,
            cancellationToken);

        return responses!.Select(response => response.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);

        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "/customers.json",
            new MaxioCreateCustomerRequest(customer),
            cancellationToken);

        return response!.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            cancellationToken,
            allowNotFound: true);

        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "/subscriptions.json",
            new MaxioCreateSubscriptionRequest(subscription),
            cancellationToken);

        return response!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"/customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return responses!.Select(response => response.Subscription).ToList();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string pathAndQuery,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
        where T : class
    {
        using var request = new HttpRequestMessage(method, BuildUri(pathAndQuery));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioApiException(null, new[] { "Maxio Advanced Billing is unavailable." }, exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MaxioApiException(response.StatusCode, await ReadErrorsAsync(response, cancellationToken));
            }

            var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
            return result ?? throw new MaxioApiException(
                response.StatusCode,
                new[] { "Maxio Advanced Billing returned an empty response." });
        }
    }

    private Uri BuildUri(string pathAndQuery)
    {
        return new Uri($"{_options.GetApiBaseUrl().TrimEnd('/')}{pathAndQuery}", UriKind.Absolute);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return new[] { $"Maxio returned HTTP {(int)response.StatusCode}." };
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return errors.EnumerateArray()
                    .Select(error => error.ValueKind == JsonValueKind.String ? error.GetString()! : error.ToString())
                    .ToList();
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                return errors.EnumerateObject()
                    .Select(error => $"{error.Name}: {error.Value.ToString().Trim('"')}")
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // The OpenAPI contract includes a few plain-text 404 responses.
        }

        return new[] { $"Maxio returned HTTP {(int)response.StatusCode}." };
    }
}
