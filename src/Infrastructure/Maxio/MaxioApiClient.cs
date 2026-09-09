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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing (Chargify) REST API.
/// Authentication is HTTP Basic with the site API key as username and "x" as
/// password; requests are addressed to https://&lt;subdomain&gt;.chargify.com
/// (or the configured <see cref="MaxioOptions.BaseUrl"/> override).
/// </summary>
public sealed class MaxioApiClient : IDisposable
{
    private const int PageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public MaxioApiClient(IOptions<MaxioOptions> options)
        : this(options, handler: null)
    {
    }

    /// <summary>
    /// Injection point for tests: allows substituting the HTTP transport.
    /// </summary>
    internal MaxioApiClient(IOptions<MaxioOptions> options, HttpMessageHandler? handler)
    {
        var maxioOptions = options.Value;
        ValidateOptions(maxioOptions);

        var baseAddress = !string.IsNullOrWhiteSpace(maxioOptions.BaseUrl)
            ? maxioOptions.BaseUrl.TrimEnd('/')
            : $"https://{maxioOptions.Subdomain.Trim().ToLowerInvariant()}.chargify.com";

        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.BaseAddress = new Uri(baseAddress + "/", UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// Lists the non-archived products that belong to the configured product family.
    /// </summary>
    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={PageSize}";
            var items = await GetAsync<IReadOnlyList<MaxioProductResponse>>(url, cancellationToken);
            if (items is null || items.Count == 0)
            {
                break;
            }

            products.AddRange(items.Select(i => i.Product).Where(p => p is not null)!);
            if (items.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    /// <summary>
    /// Returns the customer with the given reference value, or null when none exists.
    /// </summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendAsync(HttpMethod.Get, url, requestContent: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions, cancellationToken);
        return payload?.Customer;
    }

    /// <summary>
    /// Creates a billing customer. The reference value must be unique per site.
    /// </summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerBody customer, CancellationToken cancellationToken)
    {
        var payload = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerResponse>(
            "customers.json", new MaxioCreateCustomerRequest { Customer = customer }, cancellationToken);
        return payload.Customer;
    }

    /// <summary>
    /// Creates a subscription for an existing billing customer to the product with the given handle.
    /// </summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string paymentCollectionMethod, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = paymentCollectionMethod,
                UniquenessToken = Guid.NewGuid().ToString("D")
            }
        };

        var payload = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionResponse>(
            "subscriptions.json", request, cancellationToken);
        return payload.Subscription;
    }

    /// <summary>
    /// Lists all subscriptions that belong to the given billing customer.
    /// </summary>
    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = new List<MaxioSubscription>();
        var page = 1;

        while (true)
        {
            var url = $"customers/{customerId}/subscriptions.json?page={page}&per_page={PageSize}";
            var items = await GetAsync<IReadOnlyList<MaxioSubscriptionResponse>>(url, cancellationToken);
            if (items is null || items.Count == 0)
            {
                break;
            }

            subscriptions.AddRange(items.Select(i => i.Subscription).Where(s => s is not null)!);
            if (items.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return subscriptions;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, url, requestContent: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken))!;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(HttpMethod.Post, url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? requestContent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = requestContent };
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException("Could not reach the billing system.", (int)HttpStatusCode.BadGateway, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioApiException("The billing system request timed out.", (int)HttpStatusCode.GatewayTimeout, ex.Message, ex);
        }

        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(
            $"The billing system returned {(int)response.StatusCode} ({response.StatusCode}) for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}.",
            (int)response.StatusCode,
            body);
    }

    private static void ValidateOptions(MaxioOptions options)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            missing.Add(MaxioOptions.SectionName + ":ApiKey");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            missing.Add(MaxioOptions.SectionName + ":Subdomain (or " + MaxioOptions.SectionName + ":BaseUrl)");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            missing.Add(MaxioOptions.SectionName + ":ProductFamilyHandle");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing configuration is incomplete. Missing: " + string.Join(", ", missing) +
                ". Provide the values via user secrets or environment variables; do not store them in the repository.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}
