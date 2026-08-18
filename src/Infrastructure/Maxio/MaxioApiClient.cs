using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var products = new List<Product>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            var familySegment = Uri.EscapeDataString($"handle:{productFamilyHandle}");
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={perPage}";
            var pageItems = await GetAsync<List<ProductResponse>>(path, cancellationToken) ?? new List<ProductResponse>();
            foreach (var wrapper in pageItems)
            {
                if (wrapper.Product != null)
                {
                    products.Add(wrapper.Product);
                }
            }

            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadJsonAsync<CustomerResponse>(response, cancellationToken);
        return payload?.Customer;
    }

    public async Task<IReadOnlyList<Customer>> ListCustomersAsync(string query, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: GET /customers.json?q=
        var path = $"customers.json?q={Uri.EscapeDataString(query)}&per_page=50";
        var wrappers = await GetAsync<List<CustomerResponse>>(path, cancellationToken) ?? new List<CustomerResponse>();
        var customers = new List<Customer>();
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Customer != null)
            {
                customers.Add(wrapper.Customer);
            }
        }

        return customers;
    }

    public async Task<Customer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: POST /customers.json  -> 200 Customer-Response
        var request = new CreateCustomerRequest { Customer = customer };
        var payload = await PostAsync<CreateCustomerRequest, CustomerResponse>("customers.json", request, cancellationToken);
        if (payload?.Customer == null)
        {
            throw new BillingException("Maxio returned an empty customer after create.");
        }

        return payload.Customer;
    }

    public async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: GET /subscriptions/lookup.json?reference=  -> 200 or 404
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadJsonAsync<SubscriptionResponse>(response, cancellationToken);
        return payload?.Subscription;
    }

    public async Task<Subscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: POST /subscriptions.json -> 201 Subscription-Response
        var request = new CreateSubscriptionRequest { Subscription = subscription };
        var payload = await PostAsync<CreateSubscriptionRequest, SubscriptionResponse>("subscriptions.json", request, cancellationToken);
        if (payload?.Subscription == null)
        {
            throw new BillingException("Maxio returned an empty subscription after create.");
        }

        return payload.Subscription;
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Spec: GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await GetAsync<List<SubscriptionResponse>>(path, cancellationToken) ?? new List<SubscriptionResponse>();
        var subscriptions = new List<Subscription>();
        foreach (var wrapper in wrappers)
        {
            if (wrapper.Subscription != null)
            {
                subscriptions.Add(wrapper.Subscription);
            }
        }

        return subscriptions;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain (or Maxio:BaseUrl), and Maxio:ProductFamilyHandle.");
        }
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, relativePath, null, cancellationToken, allowNotFound: false);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await SendAsync(HttpMethod.Post, relativePath, content, cancellationToken, allowNotFound: false);
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken,
        bool allowNotFound)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicCredential(_options.ApiKey));
        if (content != null)
        {
            request.Content = content;
        }

        _logger.LogInformation("Maxio {Method} {Path}", method, SanitizePath(relativePath));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingException($"Unable to reach Maxio Advanced Billing: {ex.Message}");
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errors = ParseErrors(body);
        var message = errors.Count > 0
            ? $"Maxio request failed ({(int)response.StatusCode}): {string.Join("; ", errors)}"
            : $"Maxio request failed ({(int)response.StatusCode}).";

        _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}", method, SanitizePath(relativePath), (int)response.StatusCode);
        throw new BillingException(message, response.StatusCode, errors);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, MaxioJson.Options);
        }
        catch (JsonException ex)
        {
            throw new BillingException($"Maxio returned a response that could not be parsed: {ex.Message}", response.StatusCode);
        }
    }

    private static IReadOnlyList<string> ParseErrors(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ErrorListResponse>(body, MaxioJson.Options);
            return ErrorListParser.Parse(parsed?.Errors);
        }
        catch (JsonException)
        {
            return new[] { body };
        }
    }

    private static string SanitizePath(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }

    private static string BuildBasicCredential(string apiKey)
    {
        // Spec security scheme BasicAuth: username is the API key, password is `x`.
        return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
    }
}
