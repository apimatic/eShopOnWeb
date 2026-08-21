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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioBillingGateway : IBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly string _baseUrl;

    public MaxioBillingGateway(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) && string.IsNullOrWhiteSpace(_options.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        _baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl;

        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetAsync<ProductFamilyResponse>(
            $"product_families/handle:{Escape(_options.ProductFamilyHandle)}.json",
            cancellationToken);

        const int pageSize = 200;
        var page = 1;
        var plans = new List<SubscriptionPlan>();
        while (true)
        {
            var products = await GetAsync<List<ProductResponse>>(
                $"product_families/{family.ProductFamily.Id}/products.json?page={page}&per_page={pageSize}",
                cancellationToken);

            plans.AddRange(products
                .Select(wrapper => wrapper.Product)
                .Where(product => product.ArchivedAt is null)
                .Select(MapPlan));

            if (products.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToList();
    }

    public async Task<BillingCustomer?> FindCustomerAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOrNullAsync<CustomerResponse>(
            $"customers/lookup.json?reference={Escape(reference)}",
            cancellationToken);
        return response is null ? null : new BillingCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateCustomerRequest(new CreateCustomer(
            user.FirstName,
            user.LastName,
            user.Email,
            reference));
        var response = await PostAsync<CreateCustomerRequest, CustomerResponse>("customers.json", request, cancellationToken);
        return new BillingCustomer(response.Customer.Id, response.Customer.Reference);
    }

    public async Task<SubscriptionDetails?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOrNullAsync<SubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Escape(reference)}",
            cancellationToken);
        return response is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<SubscriptionDetails> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest(new CreateSubscription(
            productHandle,
            customerReference,
            subscriptionReference));
        var response = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>(
            "subscriptions.json",
            request,
            cancellationToken);
        var subscription = MapSubscription(response.Subscription);
        if (string.Equals(subscription.State, "failed_to_create", StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderValidationException("Maxio could not create the subscription.");
        }

        return subscription;
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<List<SubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        return response
            .Select(wrapper => MapSubscription(wrapper.Subscription))
            .Where(subscription => string.Equals(
                subscription.ProductFamilyHandle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await SendAsync(request, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        using var response = await SendRawAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await SendAsync(request, cancellationToken);
        return await DeserializeAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return response;
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("Maxio did not respond before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new BillingProviderException("Maxio could not be reached.", exception);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorAsync(response, cancellationToken);
        response.Dispose();
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity || response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new BillingProviderValidationException(message);
        }

        throw new BillingProviderException(message);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = $"Maxio returned HTTP {(int)response.StatusCode}.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                var detail = errors.ToString();
                return string.IsNullOrWhiteSpace(detail) ? fallback : $"Maxio rejected the request: {detail}";
            }
        }
        catch (JsonException)
        {
            // Keep provider HTML/proxy bodies out of API responses and logs.
        }

        return fallback;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new BillingProviderException("Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new BillingProviderException("Maxio returned an invalid response.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new BillingProviderException("Maxio returned an unsupported response.", exception);
        }
    }

    private Uri BuildUri(string path)
        => new($"{_baseUrl.TrimEnd('/')}/{path.TrimStart('/')}", UriKind.Absolute);

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static SubscriptionPlan MapPlan(Product product)
        => new(product.Id, product.Handle, product.Name, product.Description ?? string.Empty,
            product.PriceInCents, product.Interval, product.IntervalUnit);

    private static SubscriptionDetails MapSubscription(Subscription subscription)
    {
        var product = subscription.Product
            ?? throw new BillingProviderException("Maxio returned a subscription without a product.");
        return new SubscriptionDetails(
            subscription.Id,
            subscription.Customer.Id,
            subscription.Reference ?? subscription.Id.ToString(),
            product.ProductFamily.Handle,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents,
            product.Interval,
            product.IntervalUnit,
            subscription.State,
            subscription.NextAssessmentAt);
    }

    private sealed record ProductFamilyResponse([property: JsonPropertyName("product_family")] ProductFamily ProductFamily);
    private sealed record ProductResponse([property: JsonPropertyName("product")] Product Product);
    private sealed record CustomerResponse([property: JsonPropertyName("customer")] Customer Customer);
    private sealed record SubscriptionResponse([property: JsonPropertyName("subscription")] Subscription Subscription);
    private sealed record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateSubscriptionRequest([property: JsonPropertyName("subscription")] CreateSubscription Subscription);

    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record CreateSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_reference")] string CustomerReference,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod = "remittance");

    private sealed record ProductFamily(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("handle")] string Handle);

    private sealed record Product(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("handle")] string Handle,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] long PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string IntervalUnit,
        [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
        [property: JsonPropertyName("product_family")] ProductFamily ProductFamily);

    private sealed record Customer(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string Reference);

    private sealed record Subscription(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        [property: JsonPropertyName("customer")] Customer Customer,
        [property: JsonPropertyName("product")] Product? Product);
}
