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

internal interface IMaxioAdvancedBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken);
}

/// <summary>Small typed client built directly against maxio-spec/openapi.yaml.</summary>
internal sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioAdvancedBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        // listProductsForProductFamily: product_family_id accepts "handle:<handle>".
        var family = Uri.EscapeDataString("handle:" + _options.ProductFamilyHandle);
        using var response = await SendAsync(HttpMethod.Get, $"product_families/{family}/products.json", null, cancellationToken);
        var products = await DeserializeAsync<List<ProductResponse>>(response, cancellationToken);
        return products.Select(x => x.Product).Where(x => x is not null).Cast<MaxioProduct>().ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", null, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await DeserializeAsync<CustomerResponse>(response, cancellationToken)).Customer
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a customer response without a customer.");
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer { FirstName = firstName, LastName = lastName, Email = email, Reference = reference }
        };
        using var response = await SendAsync(HttpMethod.Post, "customers.json", request, cancellationToken);
        return (await DeserializeAsync<CustomerResponse>(response, cancellationToken)).Customer
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a customer response without a customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"customers/{customerId}/subscriptions.json", null, cancellationToken);
        var subscriptions = await DeserializeAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken);
        return subscriptions.Select(x => x.Subscription).Where(x => x is not null).Cast<MaxioSubscription>().ToArray();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        var request = new CreateSubscriptionRequest
        {
            // Collection-Method.yaml permits remittance; it is the no-card collection path for this seeded catalog.
            Subscription = new CreateSubscription { CustomerId = customerId, ProductHandle = productHandle, PaymentCollectionMethod = "remittance", Reference = reference }
        };
        using var response = await SendAsync(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return (await DeserializeAsync<MaxioSubscriptionResponse>(response, cancellationToken)).Subscription
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned a subscription response without a subscription.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var baseUri = _options.GetBaseUri();
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        var basicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredentials);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            response.Dispose();
            throw new MaxioApiException(response.StatusCode, error);
        }
        return response;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions, cancellationToken);
            if (error is not null && error.Errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join(" ", error.Errors.EnumerateArray().Select(x => x.ToString()));
            }
            if (error is not null && error.Errors.ValueKind == JsonValueKind.Object)
            {
                return string.Join(" ", error.Errors.EnumerateObject().Select(x => $"{x.Name}: {x.Value}"));
            }
        }
        catch (JsonException)
        {
            // Error formats in the contract are arrays or objects; retain a safe fallback for malformed upstream bodies.
        }
        return $"Maxio returned HTTP {(int)response.StatusCode}.";
    }
}

public sealed class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}
