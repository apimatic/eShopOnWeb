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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thin typed wrapper over the Maxio Advanced Billing (formerly Chargify) JSON API.
/// Endpoint shapes verified against the official documentation at developers.maxio.com
/// and against the live sandbox:
///   - Auth: HTTP Basic, API key as username, literal "X" as password.
///   - Customer lookup by reference: GET /customers/lookup.json?reference={r} (404 when absent).
///   - Create customer: POST /customers.json ({"customer": {...}}).
///   - Family products: GET /product_families/handle:{handle}/products.json (array of {"product": {...}}).
///   - Create subscription: POST /subscriptions.json ({"subscription": {...}}) → 201 with {"subscription": {...}}.
///   - Customer subscriptions: GET /customers/{id}/subscriptions.json (array of {"subscription": {...}}).
/// </summary>
public interface IMaxioClient
{
    /// <summary>Returns the customer with the given reference, or null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default);

    /// <summary>Lists every (non-archived) product in a product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken ct = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MaxioOptions _options;

    public MaxioClient(IHttpClientFactory httpClientFactory, IOptions<MaxioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(MaxioJson.Serializer, ct);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default)
    {
        var body = new MaxioCreateCustomerBody
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };
        var response = await SendAsync(HttpMethod.Post, "customers.json", body, ct);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(MaxioJson.Serializer, ct);
        return envelope!.Customer;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken ct = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?page={page}&per_page={PageSize}", null, ct);
            await EnsureSuccessAsync(response);
            var batch = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(MaxioJson.Serializer, ct)
                        ?? new List<MaxioProductEnvelope>();
            products.AddRange(batch.Select(b => b.Product));
            if (batch.Count < PageSize)
            {
                break;
            }
            page++;
        }
        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default)
    {
        var body = new MaxioCreateSubscriptionBody
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId
            }
        };
        var response = await SendAsync(HttpMethod.Post, "subscriptions.json", body, ct);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(MaxioJson.Serializer, ct);
        return envelope!.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;
        while (true)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}", null, ct);
            await EnsureSuccessAsync(response);
            var batch = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(MaxioJson.Serializer, ct)
                        ?? new List<MaxioSubscriptionEnvelope>();
            subscriptions.AddRange(batch.Select(b => b.Subscription));
            if (batch.Count < PageSize)
            {
                break;
            }
            page++;
        }
        return subscriptions;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(MaxioHttpClient.ClientName);
        var url = $"{ResolveBaseUrl()}/{relativeUrl}";
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body, options: MaxioJson.Serializer)
        };
        request.Headers.Authorization = CreateAuthorizationHeader(_options.ApiKey);
        var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MaxioApiException(HttpStatusCode.Unauthorized, "Maxio API rejected the credentials (401). Check Maxio:ApiKey and Maxio:Subdomain.");
        }
        return response;
    }

    /// <summary>
    /// Maxio:BaseUrl overrides the base address verbatim when set; otherwise the US
    /// production scheme is derived from the site subdomain (https://{subdomain}.chargify.com).
    /// </summary>
    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }
        return $"https://{_options.Subdomain.TrimEnd('.')}.chargify.com";
    }

    private static AuthenticationHeaderValue CreateAuthorizationHeader(string apiKey)
    {
        // Maxio Basic auth: username = API key, password = literal "X".
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var detail = await response.Content.ReadAsStringAsync();
        var errors = detail;
        try
        {
            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(detail, MaxioJson.Serializer);
            if (envelope?.Errors is { Length: > 0 })
            {
                errors = string.Join("; ", envelope.Errors);
            }
        }
        catch (JsonException)
        {
            // Not a Maxio error envelope — surface the raw body.
        }
        throw new MaxioApiException(response.StatusCode, $"Maxio API returned {(int)response.StatusCode} ({response.StatusCode}): {errors}");
    }
}

public static class MaxioHttpClient
{
    public const string ClientName = "Maxio";
}