using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioClient
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            var response = await GetAsync<ProductResponse[]>(
                $"products.json?page={page.ToString(CultureInfo.InvariantCulture)}&per_page={PageSize}",
                cancellationToken);
            products.AddRange(response.Select(item => item.Product));

            if (response.Length < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendGetWithRetryAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var result = await ReadAsync<CustomerResponse>(response, cancellationToken);
        return result.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        return (await ReadAsync<CustomerResponse>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var responses = await GetAsync<SubscriptionResponse[]>(
            $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json",
            cancellationToken);
        return responses.Select(item => item.Subscription).ToArray();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        return (await ReadAsync<SubscriptionResponse>(response, cancellationToken)).Subscription;
    }

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await SendGetWithRetryAsync(requestUri, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendGetWithRetryAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (attempt >= 2 || (response.StatusCode != HttpStatusCode.TooManyRequests &&
                                 (int)response.StatusCode < 500))
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(100 * (attempt + 1));
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, await ReadErrorAsync(response, cancellationToken));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(
            HttpStatusCode.BadGateway,
            "Maxio returned an empty or invalid response.");
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join(" ", errors.EnumerateArray().Select(ErrorText)),
                    JsonValueKind.String => errors.GetString() ?? "Maxio rejected the request.",
                    JsonValueKind.Object => string.Join(" ", errors.EnumerateObject().Select(
                        property => $"{property.Name}: {ErrorText(property.Value)}")),
                    _ => "Maxio rejected the request."
                };
            }
        }
        catch (JsonException)
        {
            // Keep upstream HTML and malformed payloads out of the public API response.
        }

        return $"Maxio request failed with status {(int)response.StatusCode}.";
    }

    private static string ErrorText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(ErrorText)),
        _ => element.ToString()
    };
}
