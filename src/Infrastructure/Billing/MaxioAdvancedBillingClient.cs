using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioAdvancedBillingClient> _logger;

    public MaxioAdvancedBillingClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioAdvancedBillingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey) && _httpClient.DefaultRequestHeaders.Authorization is null)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (_httpClient.BaseAddress is null &&
            (!string.IsNullOrWhiteSpace(_options.BaseUrl) || !string.IsNullOrWhiteSpace(_options.Subdomain)))
        {
            _httpClient.BaseAddress = _options.ResolveBaseAddress();
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var plans = new List<SubscriptionPlan>();
        const int perPage = 200;
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page={perPage}";
            var wrapped = await SendAsync<List<MaxioProductResponse>>(HttpMethod.Get, path, null, cancellationToken);
            var batch = wrapped?
                .Select(item => item.Product)
                .Where(product => product is not null && string.IsNullOrWhiteSpace(product.ArchivedAt))
                .Select(product => MapPlan(product!))
                .Where(plan => !string.IsNullOrWhiteSpace(plan.Handle))
                .ToList() ?? new List<SubscriptionPlan>();

            plans.AddRange(batch);

            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return response?.Customer is null ? null : MapCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        CreateBillingCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference
            }
        };

        var response = await SendAsync<MaxioCustomerResponse>(
            HttpMethod.Post, "customers.json", payload, cancellationToken);

        if (response?.Customer is null)
        {
            throw new MaxioApiException(500, "Maxio createCustomer returned an empty customer.");
        }

        return MapCustomer(response.Customer);
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Get, path, null, cancellationToken, treatNotFoundAsNull: true);

        return response?.Subscription is null ? null : MapSubscription(response.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        CreateBillingSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionBody
            {
                ProductHandle = request.ProductHandle,
                CustomerId = request.CustomerId,
                Reference = request.Reference,
                PaymentCollectionMethod = request.PaymentCollectionMethod
            }
        };

        var response = await SendAsync<MaxioSubscriptionResponse>(
            HttpMethod.Post, "subscriptions.json", payload, cancellationToken);

        if (response?.Subscription is null)
        {
            throw new MaxioApiException(500, "Maxio createSubscription returned an empty subscription.");
        }

        return MapSubscription(response.Subscription);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var wrapped = await SendAsync<List<MaxioSubscriptionResponse>>(HttpMethod.Get, path, null, cancellationToken);

        return wrapped?
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .ToList() ?? new List<ShopperSubscription>();
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken,
        bool treatNotFoundAsNull = false)
    {
        _options.EnsureApiKey();
        _httpClient.BaseAddress ??= _options.ResolveBaseAddress();

        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && treatNotFoundAsNull)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = FormatError(response.StatusCode, payload);
            _logger.LogWarning("Maxio {Method} {Path} failed with {StatusCode}.", method, relativePath, (int)response.StatusCode);
            throw new MaxioApiException((int)response.StatusCode, message);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static string FormatError(HttpStatusCode statusCode, string payload)
    {
        try
        {
            var errors = JsonSerializer.Deserialize<MaxioErrorListResponse>(payload, JsonOptions);
            if (errors?.Errors is { Count: > 0 })
            {
                return $"Maxio returned {(int)statusCode}: {string.Join("; ", errors.Errors)}";
            }
        }
        catch (JsonException)
        {
            // Fall through to a truncated raw body. Never include credentials.
        }

        var snippet = payload.Length > 500 ? payload[..500] : payload;
        return $"Maxio returned {(int)statusCode}: {snippet}";
    }

    private static SubscriptionPlan MapPlan(MaxioProductJson product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequireCreditCard = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static BillingCustomer MapCustomer(MaxioCustomerJson customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email ?? string.Empty,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty
    };

    private static ShopperSubscription MapSubscription(MaxioSubscriptionJson subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        NextBillingAt = ParseTimestamp(subscription.NextAssessmentAt),
        CurrentPeriodEndsAt = ParseTimestamp(subscription.CurrentPeriodEndsAt),
        CreatedAt = ParseTimestamp(subscription.CreatedAt)
    };

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
