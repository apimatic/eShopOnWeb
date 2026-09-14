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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? $"https://{settings.Subdomain}.chargify.com"
            : settings.BaseUrl;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        // maxio-spec path: GET /product_families/{product_family_id}/products.json
        var family = $"handle:{Uri.EscapeDataString(productFamilyHandle)}";
        var responses = await SendAsync<List<MaxioProductResponse>>(
            HttpMethod.Get,
            $"product_families/{family}/products.json",
            null,
            cancellationToken);
        return responses.Select(x => x.Product).ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        // maxio-spec path: GET /customers/lookup.json?reference={reference}
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await SendRequestAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return payload.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        // maxio-spec path/schema: POST /customers.json, Create-Customer-Request
        var payload = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);
        return payload.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        // maxio-spec path: GET /customers/{customer_id}/subscriptions.json
        var responses = await SendAsync<List<MaxioSubscriptionResponse>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return responses.Select(x => x.Subscription).ToArray();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        // maxio-spec path/schema: POST /subscriptions.json, Create-Subscription-Request
        var payload = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);
        return payload.Subscription;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await SendRequestAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, "Maxio did not respond before the timeout.");
        }
        catch (HttpRequestException)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio could not be reached.");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new MaxioApiException(response.StatusCode, error);
        }

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return payload ?? throw new MaxioApiException(
                HttpStatusCode.BadGateway,
                "Maxio returned an empty or invalid response.");
        }
        catch (JsonException)
        {
            throw new MaxioApiException(
                HttpStatusCode.BadGateway,
                "Maxio returned an empty or invalid response.");
        }
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
                var message = errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(x => x.ToString())),
                    JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}")),
                    _ => errors.ToString()
                };
                if (message.Length > 500)
                {
                    message = message[..500] + "…";
                }

                return $"Maxio rejected the request: {message}";
            }
        }
        catch (JsonException)
        {
            // Return a stable message rather than exposing an upstream HTML/error body.
        }

        return $"Maxio request failed with HTTP {(int)response.StatusCode}.";
    }
}
