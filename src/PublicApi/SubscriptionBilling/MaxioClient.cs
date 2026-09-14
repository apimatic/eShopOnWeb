using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _baseUrl = options.Value.ApiBaseUrl.TrimEnd('/');
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSiteResponse>(HttpMethod.Get, "site.json", null, false, cancellationToken);
        return response!.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            var response = await SendAsync<List<MaxioProductResponse>>(
                HttpMethod.Get,
                $"products.json?page={page}&per_page={PageSize}&include_archived=false",
                null,
                false,
                cancellationToken);
            var pageProducts = response!.Select(item => item.Product).ToList();
            products.AddRange(pageProducts);

            if (pageProducts.Count < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            false,
            cancellationToken);
        return response!.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            false,
            cancellationToken);
        return response!.Select(item => item.Subscription).ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            true,
            cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            false,
            cancellationToken);
        return response!.Subscription;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}/{relativeUrl}");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioApiException("Maxio Advanced Billing could not be reached.", innerException: exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errors = await ReadErrorsAsync(response, cancellationToken);
                throw new MaxioApiException(
                    "Maxio Advanced Billing rejected the request.",
                    (int)response.StatusCode,
                    errors);
            }

            var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            if (result is null)
            {
                throw new MaxioApiException("Maxio Advanced Billing returned an empty or invalid response.", (int)response.StatusCode);
            }

            return result;
        }
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            var messages = new List<string>();
            CollectStrings(errors, messages);
            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void CollectStrings(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var message = element.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    messages.Add(message);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, messages);
                }
                break;
        }
    }
}
