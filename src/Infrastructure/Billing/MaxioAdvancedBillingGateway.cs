using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing HTTP client. Paths, query params, bodies, and auth match
/// maxio-spec/openapi.yaml (BasicAuth username = API key, password = "x").
/// </summary>
public class MaxioAdvancedBillingGateway : IAdvancedBillingGateway
{
    private const int MaxPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingGateway> _logger;

    public MaxioAdvancedBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        ConfigureHttpClient();
    }

    public async Task<IReadOnlyList<BillingProduct>> ListCatalogPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyId = $"handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}";
        var products = new List<BillingProduct>();

        for (var page = 1; ; page++)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={MaxPageSize}";
            var pageItems = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken)
                            ?? new List<MaxioProductResponse>();

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(ToProduct(item.Product));
                }
            }

            if (pageItems.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<BillingProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"products/handle/{Uri.EscapeDataString(productHandle)}.json";
        var response = await GetAsync<MaxioProductResponse>(path, cancellationToken, allowNotFound: true);
        return response?.Product is null ? null : ToProduct(response.Product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioCustomerResponse>(path, cancellationToken, allowNotFound: true);
        return response?.Customer is null ? null : ToCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await SendJsonAsync<MaxioCustomerResponse>(
            HttpMethod.Post,
            "customers.json",
            body,
            cancellationToken);

        if (response?.Customer is null)
        {
            throw new BillingGatewayException("Maxio createCustomer returned an empty customer.", 200);
        }

        return ToCustomer(response.Customer);
    }

    public async Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioSubscriptionResponse>(path, cancellationToken, allowNotFound: true);
        return response?.Subscription is null ? null : ToSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await GetAsync<List<MaxioSubscriptionResponse>>(path, cancellationToken)
                    ?? new List<MaxioSubscriptionResponse>();

        var subscriptions = new List<BillingSubscription>(items.Count);
        foreach (var item in items)
        {
            if (item.Subscription is not null)
            {
                subscriptions.Add(ToSubscription(item.Subscription));
            }
        }

        return subscriptions;
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };

        var response = await SendJsonAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post,
            "subscriptions.json",
            body,
            cancellationToken);

        if (response?.Subscription is null)
        {
            throw new BillingGatewayException("Maxio createSubscription returned an empty subscription.", 201);
        }

        return ToSubscription(response.Subscription);
    }

    private void ConfigureHttpClient()
    {
        if (!_options.IsConfigured)
        {
            return;
        }

        _httpClient.BaseAddress = _options.ResolveBaseAddress();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new BillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle, and Maxio:Subdomain or Maxio:BaseUrl.");
        }

        if (_httpClient.BaseAddress is null)
        {
            ConfigureHttpClient();
        }
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        using var response = await SendWithRetryAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken, allowNotFound);
    }

    private async Task<T?> SendJsonAsync<T>(HttpMethod method, string relativePath, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, MaxioJson.SerializerOptions);
        using var request = new HttpRequestMessage(method, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await SendWithRetryAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            var attemptRequest = await CloneAsync(request);
            response = await _httpClient.SendAsync(attemptRequest, cancellationToken);

            if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
            {
                return response;
            }

            _logger.LogWarning("Transient Maxio response {StatusCode} for {Method} {Path}; retry {Attempt}/{Max}.",
                (int)response.StatusCode, request.Method, request.RequestUri, attempt, maxAttempts);

            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }

        return response!;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == (HttpStatusCode)429
        || (int)statusCode >= 500;

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;

        if (status < 200 || status > 299)
        {
            var message = FormatError(response.StatusCode, payload);
            _logger.LogWarning("Maxio {Method} {Uri} failed with {StatusCode}: {Message}",
                response.RequestMessage?.Method, response.RequestMessage?.RequestUri, status, message);
            throw new BillingGatewayException(message, status);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, MaxioJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new BillingGatewayException("Maxio returned a response that could not be parsed.", status, ex);
        }
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        var details = ExtractErrorMessages(payload);
        if (!string.IsNullOrWhiteSpace(details))
        {
            return $"Maxio Advanced Billing request failed ({(int)statusCode}): {details}";
        }

        return $"Maxio Advanced Billing request failed ({(int)statusCode}).";
    }

    private static string ExtractErrorMessages(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("errors", out var errors))
            {
                return payload.Trim();
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in errors.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parts.Add(value);
                        }
                    }
                    else
                    {
                        parts.Add(item.ToString());
                    }
                }

                return string.Join(" ", parts);
            }

            if (errors.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var property in errors.EnumerateObject())
                {
                    parts.Add($"{property.Name}: {property.Value}");
                }

                return string.Join(" ", parts);
            }

            if (errors.ValueKind == JsonValueKind.String)
            {
                return errors.GetString() ?? string.Empty;
            }

            return payload.Trim();
        }
        catch (JsonException)
        {
            return payload.Trim();
        }
    }

    private static BillingCustomer ToCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static BillingProduct ToProduct(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static BillingSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        Reference = subscription.Reference,
        ProductPriceInCents = subscription.ProductPriceInCents,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Product = subscription.Product is null ? null : ToProduct(subscription.Product)
    };

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
