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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// A deliberately small client for the operations used from maxio-spec/openapi.yaml.
/// </summary>
public sealed class MaxioApiClient
{
    // The OpenAPI schemas use snake_case for both request and response members.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _options.Validate();
    }

    // GET /products.json (listProducts)
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions, cancellationToken)
            ?? new List<MaxioProductResponse>();
        return payload.Where(x => x.Product is not null).Select(x => x.Product!).ToArray();
    }

    // GET /customers/lookup.json?reference= (readCustomerByReference)
    public async Task<MaxioCustomerData?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken))?.Customer;
    }

    // POST /customers.json (createCustomer)
    public async Task<MaxioCustomerData> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/customers.json", cancellationToken, new { customer });
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return payload?.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty customer response.");
    }

    // GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions)
    public async Task<IReadOnlyList<MaxioSubscriptionData>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions, cancellationToken)
            ?? new List<MaxioSubscriptionResponse>();
        return payload.Where(x => x.Subscription is not null).Select(x => x.Subscription!).ToArray();
    }

    // POST /subscriptions.json (createSubscription)
    public async Task<MaxioSubscriptionData> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        // The configured catalog permits no-card enrollment. Remittance is a Collection-Method
        // value declared by the Maxio contract and prevents an immediate card capture.
        var subscription = new MaxioCreateSubscription(productHandle, customerId, "remittance");
        using var response = await SendAsync(HttpMethod.Post, "/subscriptions.json", cancellationToken, new { subscription });
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions, cancellationToken);
        return payload?.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken, object? body = null)
    {
        var endpoint = new Uri(_options.ApiBaseAddress().ToString().TrimEnd('/') + path, UriKind.Absolute);
        using var request = new HttpRequestMessage(method, endpoint);
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        // Do not surface Maxio's body: it can include business/customer information.
        await response.Content.LoadIntoBufferAsync();
        throw new MaxioApiException(response.StatusCode, "Maxio did not accept the request.");
    }
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}

public sealed record MaxioCustomerResponse(MaxioCustomerData? Customer);
public sealed record MaxioCustomerData(long Id, string FirstName, string LastName, string Email, string? Reference);
public sealed record MaxioCreateCustomer(string FirstName, string LastName, string Email, string Reference);
public sealed record MaxioProductResponse(MaxioProduct? Product);
public sealed record MaxioProduct(long Id, string Name, string? Handle, long PriceInCents, int Interval,
    string IntervalUnit, DateTimeOffset? ArchivedAt, MaxioProductFamily? ProductFamily);
public sealed record MaxioProductFamily(long Id, string Handle);
public sealed record MaxioSubscriptionResponse(MaxioSubscriptionData? Subscription);
public sealed record MaxioSubscriptionData(long Id, string State, long ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? NextAssessmentAt, MaxioProduct? Product);
public sealed record MaxioCreateSubscription(string ProductHandle, long CustomerId, string PaymentCollectionMethod);
