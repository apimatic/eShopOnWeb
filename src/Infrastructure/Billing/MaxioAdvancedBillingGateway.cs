using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioAdvancedBillingGateway : IAdvancedBillingGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioAdvancedBillingGateway> _logger;

    public MaxioAdvancedBillingGateway(HttpClient httpClient, ILogger<MaxioAdvancedBillingGateway> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var plans = new List<SubscriptionPlan>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/{familyId}/products.json?page={page}&per_page=200";
            var pageItems = await GetAsync<List<MaxioProductResponse>>(path, cancellationToken, HttpStatusCode.NotFound)
                ?? new List<MaxioProductResponse>();

            foreach (var wrapper in pageItems)
            {
                var product = wrapper.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle,
                    Name = product.Name ?? product.Handle,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit ?? "month"
                });
            }

            if (pageItems.Count < 200)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioCustomerResponse>(path, cancellationToken, HttpStatusCode.NotFound);
        return MapCustomer(response?.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var response = await PostAsync<MaxioCustomerResponse>("customers.json", payload, cancellationToken, HttpStatusCode.OK);
        return MapCustomer(response?.Customer) ?? throw new AdvancedBillingException("Maxio created a customer without a body.", 502);
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var items = await GetAsync<List<MaxioSubscriptionResponse>>(path, cancellationToken)
            ?? new List<MaxioSubscriptionResponse>();

        return items
            .Select(item => MapSubscription(item.Subscription))
            .Where(subscription => subscription is not null)
            .Cast<ShopperSubscription>()
            .ToList();
    }

    public async Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await GetAsync<MaxioSubscriptionResponse>(path, cancellationToken, HttpStatusCode.NotFound);
        return MapSubscription(response?.Subscription);
    }

    public async Task<ShopperSubscription> CreateSubscriptionAsync(
        CreateBillingSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = subscription.ProductHandle,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            }
        };

        var response = await PostAsync<MaxioSubscriptionResponse>(
            "subscriptions.json",
            payload,
            cancellationToken,
            HttpStatusCode.Created,
            HttpStatusCode.OK);

        return MapSubscription(response?.Subscription)
            ?? throw new AdvancedBillingException("Maxio created a subscription without a body.", 502);
    }

    private async Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken, params HttpStatusCode[] extraSuccess)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        return await SendAsync<T>(request, cancellationToken, retryOnTransient: true, extraSuccess);
    }

    private async Task<T?> PostAsync<T>(string relativePath, object payload, CancellationToken cancellationToken, params HttpStatusCode[] extraSuccess)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return await SendAsync<T>(request, cancellationToken, retryOnTransient: false, extraSuccess);
    }

    private async Task<T?> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool retryOnTransient,
        params HttpStatusCode[] extraSuccess)
    {
        const int maxAttempts = 3;
        HttpResponseMessage? response = null;
        var attempt = 0;

        while (true)
        {
            attempt++;
            response?.Dispose();

            HttpRequestMessage attemptRequest;
            if (attempt == 1)
            {
                attemptRequest = request;
            }
            else
            {
                attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            }

            _logger.LogInformation("Maxio {Method} {Path} (attempt {Attempt})", attemptRequest.Method, attemptRequest.RequestUri, attempt);
            response = await _httpClient.SendAsync(attemptRequest, cancellationToken);

            if (retryOnTransient && attempt < maxAttempts && IsTransient(response.StatusCode))
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > delay)
                {
                    delay = retryAfter;
                }

                _logger.LogWarning("Maxio returned {StatusCode}; retrying in {DelayMs}ms.", (int)response.StatusCode, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            break;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound && extraSuccess.Contains(HttpStatusCode.NotFound))
            {
                return default;
            }

            if (response.IsSuccessStatusCode || extraSuccess.Contains(response.StatusCode))
            {
                if (string.IsNullOrWhiteSpace(body))
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }

            throw new AdvancedBillingException(FormatError(response.StatusCode, body), (int)response.StatusCode, body);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.BadGateway
        || statusCode == HttpStatusCode.ServiceUnavailable
        || statusCode == HttpStatusCode.GatewayTimeout;

    private static string FormatError(HttpStatusCode statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Maxio Advanced Billing returned {(int)statusCode} {statusCode}.";
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorListResponse>(body, JsonOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return string.Join(" ", parsed.Errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body when Maxio returns an unexpected error shape.
        }

        return body.Length > 500 ? body[..500] : body;
    }

    private static BillingCustomer? MapCustomer(MaxioCustomer? customer)
    {
        if (customer is null || customer.Id == 0)
        {
            return null;
        }

        return new BillingCustomer
        {
            Id = customer.Id,
            Reference = customer.Reference,
            Email = customer.Email ?? string.Empty
        };
    }

    private static ShopperSubscription? MapSubscription(MaxioSubscription? subscription)
    {
        if (subscription is null || subscription.Id == 0)
        {
            return null;
        }

        var priceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new ShopperSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = priceInCents,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            Reference = subscription.Reference
        };
    }
}
