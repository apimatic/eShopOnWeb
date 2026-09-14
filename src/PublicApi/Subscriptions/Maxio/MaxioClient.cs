using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        using var response = await _httpClient.GetAsync(
            $"product_families/{family}/products.json",
            cancellationToken);

        var result = await ReadAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return result.Select(item => item.Product).ToList();
    }

    public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        return GetOptionalAsync<MaxioCustomerEnvelope, MaxioCustomer>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Customer,
            cancellationToken);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerDetails customer,
        CancellationToken cancellationToken)
    {
        var request = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            request,
            JsonOptions,
            cancellationToken);

        return (await ReadAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        return GetOptionalAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            envelope => envelope.Subscription,
            cancellationToken);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionDetails subscription,
        CancellationToken cancellationToken)
    {
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            request,
            JsonOptions,
            cancellationToken);

        return (await ReadAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    public Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        return GetOptionalAsync<MaxioSubscriptionEnvelope, MaxioSubscription>(
            $"subscriptions/{subscriptionId}.json",
            envelope => envelope.Subscription,
            cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        var result = await ReadAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return result.Select(item => item.Subscription).ToList();
    }

    private async Task<TResult?> GetOptionalAsync<TEnvelope, TResult>(
        string requestUri,
        Func<TEnvelope, TResult> selector,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return selector(await ReadAsync<TEnvelope>(response, cancellationToken));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errors = await ReadErrorsAsync(response, cancellationToken);
            throw new MaxioApiException(
                response.StatusCode,
                $"Maxio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                errors);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(
            response.StatusCode,
            "Maxio returned an empty or invalid response.");
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return Array.Empty<string>();
            }

            return errors.ValueKind switch
            {
                JsonValueKind.Array => errors.EnumerateArray()
                    .Select(error => error.ToString())
                    .ToList(),
                JsonValueKind.Object => errors.EnumerateObject()
                    .Select(error => $"{error.Name}: {error.Value}")
                    .ToList(),
                JsonValueKind.String => new[] { errors.GetString() ?? string.Empty },
                _ => Array.Empty<string>()
            };
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
