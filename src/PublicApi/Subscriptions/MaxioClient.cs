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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Small, typed HTTP client for the Maxio Advanced Billing REST resources used by eShopOnWeb.
/// The API key is never included in logs or exception messages.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new SubscriptionConfigurationException("Maxio:ApiKey is required.");
        }

        _baseUri = _options.GetBaseUri();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private readonly Uri _baseUri;

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var family = await GetAsync<MaxioProductFamilyResponse>(
            $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}.json",
            "read product family",
            cancellationToken);

        var products = await GetAsync<List<MaxioProductResponse>>(
            $"product_families/{family.ProductFamily.Id}/products.json?page=1&per_page=200",
            "list products",
            cancellationToken);

        return products
            .Where(item => item.Product is not null && string.IsNullOrWhiteSpace(item.Product.ArchivedAt))
            .Select(item => item.Product)
            .ToArray();
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            "look up customer",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer");
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(_jsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var envelope = await PostAsync<MaxioCustomerResponse>(
            "customers.json",
            body,
            uniquenessToken,
            "create customer",
            cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            "list customer subscriptions",
            cancellationToken);
        return subscriptions.Select(item => item.Subscription).ToArray();
    }

    public async Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            null,
            "look up subscription",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up subscription");
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(_jsonOptions, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = reference
            }
        };

        var envelope = await PostAsync<MaxioSubscriptionResponse>(
            "subscriptions.json",
            body,
            uniquenessToken,
            "create subscription",
            cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T> GetAsync<T>(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, operation, cancellationToken);
        await EnsureSuccessAsync(response, operation);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, operation);
    }

    private async Task<T> PostAsync<T>(string path, object body, string uniquenessToken, string operation, CancellationToken cancellationToken)
    {
        var pathWithToken = $"{path}?uniqueness_token={Uri.EscapeDataString(uniquenessToken)}";
        using var response = await SendAsync(HttpMethod.Post, pathWithToken, JsonContent.Create(body, options: _jsonOptions), operation, cancellationToken);
        await EnsureSuccessAsync(response, operation);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new MaxioApiException((int)response.StatusCode, operation);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri.ToString().TrimEnd('/') + "/" + path));
        request.Content = content;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:X"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Maxio request failed during {Operation}.", operation);
            throw new MaxioApiException(502, operation);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Maxio request timed out during {Operation}.", operation);
            throw new MaxioApiException(504, operation);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync();
            throw new MaxioApiException((int)response.StatusCode, operation);
        }
    }
}
