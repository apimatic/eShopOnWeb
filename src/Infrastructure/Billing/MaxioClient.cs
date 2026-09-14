using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var settings = options.Value;
        var configuredBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? $"https://{settings.Subdomain}.chargify.com"
            : settings.BaseUrl;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(configuredBaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("eShopOnWeb-Maxio/1.0");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string familyHandle,
        CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        var responses = await GetRequiredAsync<List<MaxioProductResponse>>(path, cancellationToken);
        return responses.Select(x => x.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOptionalAsync<MaxioCustomerResponse>(path, cancellationToken);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json",
            new MaxioCreateCustomerRequest { Customer = customer },
            cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetOptionalAsync<MaxioSubscriptionResponse>(path, cancellationToken);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json",
            new MaxioCreateSubscriptionRequest { Subscription = subscription },
            cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var responses = await GetRequiredAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        return responses.Select(x => x.Subscription).ToList();
    }

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => _httpClient.GetAsync(path, cancellationToken));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => _httpClient.GetAsync(path, cancellationToken));
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken));
        return await ReadRequiredAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new BillingProviderException("Maxio could not be reached.", exception);
        }
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new BillingProviderException("Maxio returned an empty or invalid response.");
    }

    private static async Task<BillingProviderException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var detail = string.Empty;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                detail = errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(x => x.ToString())),
                    JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}")),
                    _ => errors.ToString()
                };
            }
        }
        catch (JsonException)
        {
            // Error payloads are provider-controlled; an invalid one must not hide the HTTP status.
        }

        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new BillingProviderException(
            $"Maxio rejected the request with HTTP {(int)response.StatusCode}.{suffix}");
    }
}
