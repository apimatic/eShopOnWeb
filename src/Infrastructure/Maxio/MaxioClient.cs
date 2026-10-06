using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing API. Authenticates with HTTP Basic auth using the
/// API key as the username and "X" as the password, per the Maxio documentation.
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const string BasicAuthPassword = "X";

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MaxioClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    private string BaseAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return _settings.BaseUrl.TrimEnd('/');
            }

            return $"https://{_settings.Subdomain}.chargify.com";
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, _jsonOptions);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer?> GetCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/customers/{customerId}.json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, _jsonOptions);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft draft, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerDto
            {
                FirstName = draft.FirstName,
                LastName = draft.LastName,
                Email = draft.Email,
                Reference = draft.Reference
            }
        };

        using var request = CreateRequest(HttpMethod.Post, "/customers.json", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, _jsonOptions);
        return MapCustomer(envelope?.Customer ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty customer payload.", body));
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionDto
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference
            },
            UniquenessToken = uniquenessToken
        };

        using var request = CreateRequest(HttpMethod.Post, "/subscriptions.json", payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(body, _jsonOptions);
        return MapSubscription(envelope?.Subscription ?? throw new MaxioApiException((int)response.StatusCode, "Maxio returned an empty subscription payload.", body));
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(body, _jsonOptions);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/subscriptions/{subscriptionId}.json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(body, _jsonOptions);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionListItem>>(body, _jsonOptions) ?? new List<MaxioSubscriptionListItem>();
        return items.ConvertAll(item => MapSubscription(item.Subscription));
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var items = JsonSerializer.Deserialize<List<MaxioProductListItem>>(body, _jsonOptions) ?? new List<MaxioProductListItem>();
        return items.ConvertAll(item => MapProduct(item.Product));
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/products/handle/{Uri.EscapeDataString(handle)}.json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, request, body);
        var envelope = JsonSerializer.Deserialize<MaxioProductEnvelope>(body, _jsonOptions);
        return envelope?.Product is null ? null : MapProduct(envelope.Product);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? payload = null)
    {
        var request = new HttpRequestMessage(method, $"{BaseAddress}{path}");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:{BasicAuthPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, HttpRequestMessage request, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new MaxioApiException(
            (int)response.StatusCode,
            $"Maxio API returned {(int)response.StatusCode} for {request.Method} {request.RequestUri}.",
            body);
    }

    private static MaxioCustomer MapCustomer(MaxioCustomerDto dto)
    {
        return new MaxioCustomer
        {
            Id = dto.Id,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Reference = dto.Reference
        };
    }

    private static MaxioSubscription MapSubscription(MaxioSubscriptionDto dto)
    {
        return new MaxioSubscription
        {
            Id = dto.Id,
            State = dto.State,
            ProductPriceInCents = dto.ProductPriceInCents,
            CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
            NextAssessmentAt = dto.NextAssessmentAt,
            CreatedAt = dto.CreatedAt,
            Reference = dto.Reference,
            Customer = dto.Customer is null ? null : MapCustomer(dto.Customer),
            Product = dto.Product is null ? null : MapProduct(dto.Product)
        };
    }

    private static MaxioProduct MapProduct(MaxioProductDto dto)
    {
        return new MaxioProduct
        {
            Id = dto.Id,
            Name = dto.Name,
            Handle = dto.Handle,
            Description = dto.Description,
            PriceInCents = dto.PriceInCents,
            Interval = dto.Interval,
            IntervalUnit = dto.IntervalUnit,
            ProductFamilyHandle = dto.ProductFamily?.Handle
        };
    }
}
