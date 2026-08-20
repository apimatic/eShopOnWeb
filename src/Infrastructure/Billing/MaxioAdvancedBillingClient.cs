using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP client for Maxio Advanced Billing. Auth is HTTP Basic with the API key as
/// username and <c>x</c> as password, per the OpenAPI security scheme.
/// </summary>
public sealed class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private const int MaxPageSize = 200;
    private readonly HttpClient _httpClient;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProductSnapshot>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        // GET /product_families/{product_family_id}/products.json
        // product_family_id: "Either the product family's id or its handle prefixed with `handle:`"
        var products = new List<MaxioProductSnapshot>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={MaxPageSize}";
            var wrappers = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
            var batch = wrappers?
                .Select(wrapper => wrapper.Product)
                .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(MapProduct)!
                .ToList() ?? new List<MaxioProductSnapshot>();

            products.AddRange(batch);
            if (batch.Count < MaxPageSize)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomerSnapshot?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/lookup.json?reference=
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<MaxioCustomerSnapshot> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default)
    {
        // POST /customers.json
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", payload, cancellationToken);
        if (response?.Customer is null)
        {
            throw new BillingProviderException(502, "Maxio createCustomer returned an empty customer.");
        }

        return MapCustomer(response.Customer);
    }

    public async Task<MaxioSubscriptionSnapshot> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? reference,
        string? paymentCollectionMethod,
        CancellationToken cancellationToken = default)
    {
        // POST /subscriptions.json
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Post, "subscriptions.json", payload, cancellationToken);
        if (response?.Subscription is null)
        {
            throw new BillingProviderException(502, "Maxio createSubscription returned an empty subscription.");
        }

        return MapSubscription(response.Subscription);
    }

    public async Task<MaxioSubscriptionSnapshot?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        // GET /subscriptions/lookup.json?reference=
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioSubscriptionResponse>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionSnapshot>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        // GET /customers/{customer_id}/subscriptions.json
        var path = $"customers/{customerId}/subscriptions.json";
        var wrappers = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);
        return wrappers?
            .Select(wrapper => wrapper.Subscription)
            .Where(subscription => subscription is not null)
            .Select(MapSubscription)!
            .ToList() ?? new List<MaxioSubscriptionSnapshot>();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        var options = _options.Value;
        options.EnsureConfigured();

        var baseUri = new Uri(options.ResolveApiBaseUrl().TrimEnd('/') + "/");
        Exception? lastTransient = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }

            using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x")));

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                lastTransient = ex;
                _logger.LogWarning(ex, "Transient failure calling Maxio {Method} {Path} (attempt {Attempt})", method, relativePath, attempt + 1);
                continue;
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if (IsTransient(response.StatusCode) && attempt < 2)
                {
                    _logger.LogWarning(
                        "Transient Maxio status {StatusCode} for {Method} {Path} (attempt {Attempt})",
                        (int)response.StatusCode, method, relativePath, attempt + 1);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var messages = MaxioErrorParser.ReadMessages(content);
                    var detail = messages.Count > 0 ? string.Join(" ", messages) : response.ReasonPhrase ?? "Unknown error";
                    _logger.LogWarning(
                        "Maxio {Method} {Path} failed with {StatusCode}: {Detail}",
                        method, relativePath, (int)response.StatusCode, detail);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new BillingConfigurationException(
                            "Maxio rejected the API key. Check Maxio:ApiKey / MAXIO_API_KEY.");
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new BillingValidationException(detail);
                    }

                    throw new BillingProviderException((int)response.StatusCode, detail);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(content, MaxioJson.SerializerOptions);
            }
        }

        if (lastTransient is not null)
        {
            throw new BillingProviderException(503, "Maxio was unreachable. Please try again.");
        }

        throw new BillingProviderException(503, "Maxio request failed after retries.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static MaxioProductSnapshot MapProduct(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static MaxioCustomerSnapshot MapCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        FirstName = customer.FirstName,
        LastName = customer.LastName,
        Email = customer.Email,
        Reference = customer.Reference
    };

    private static MaxioSubscriptionSnapshot MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        NextAssessmentAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name
    };
}
